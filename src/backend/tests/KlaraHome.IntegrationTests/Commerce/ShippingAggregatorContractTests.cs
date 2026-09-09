using System.Net;
using System.Net.Http.Json;
using KlaraHome.Modules.Shipping.Infrastructure;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Courier.Shiprocket;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The aggregator adapter against a sandbox account: serviceability, booking, label, manifest,
/// pickup, cancel and tracking, plus one token refresh on a <c>401</c>.
/// </summary>
/// <remarks>
/// <para>
/// The step card's own Known Gaps section is explicit: <b>no Shiprocket account exists</b>
/// (docs/08-integrations.md §7, parked at Step 16A → Step 32). There is therefore no live sandbox
/// this row can be proved against, and pretending otherwise would be a false close.
/// </para>
/// <para>
/// What honestly closes here: every one of the seven operations — serviceability, booking, label,
/// manifest, pickup, cancel, tracking — is exercised through <see cref="FakeShippingProvider"/>
/// elsewhere in this file across <c>ShippingBookingTests</c>, <c>ShippingLabelTests</c> and
/// <c>ShippingWebhookTests</c>, which is the same substitute the module itself was unit-tested
/// against. The <b>token-refresh-on-401</b> behaviour is specific to
/// <see cref="ShiprocketShippingProvider"/>'s own HTTP plumbing, which the fake does not model at
/// all — so it is proved here directly against that class, with a scripted handler standing in for
/// Shiprocket's HTTP surface. This is a contract test on the adapter's own retry policy, not a claim
/// that Shiprocket's actual API behaves this way — that half stays open until an account exists.
/// </para>
/// </remarks>
public sealed class ShippingAggregatorContractTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// A <c>401</c> from the courier triggers exactly one token refresh and one retry, which then
    /// succeeds.
    /// </summary>
    [Fact]
    public async Task A_401_triggers_exactly_one_token_refresh_and_one_retry()
    {
        var handler = new ScriptedHandler();

        // First login: token "T1".
        handler.Enqueue(HttpMethod.Post, "/v1/external/auth/login", HttpStatusCode.OK, """{"token":"T1"}""");

        // The API call with T1 is refused as stale.
        handler.Enqueue(HttpMethod.Post, "/v1/external/orders/cancel", HttpStatusCode.Unauthorized, "{}");

        // The adapter refreshes: a second login, token "T2".
        handler.Enqueue(HttpMethod.Post, "/v1/external/auth/login", HttpStatusCode.OK, """{"token":"T2"}""");

        // The retry with T2 succeeds. Only one retry is attempted: a second 401 here would be left
        // to fail rather than looping.
        handler.Enqueue(HttpMethod.Post, "/v1/external/orders/cancel", HttpStatusCode.OK, "{}");

        var options = new FixedShippingOptions(new ShippingOptions
        {
            BaseUrl = "https://apiv2.shiprocket.in",
            Provider = "shiprocket",
            ApiUser = "sandbox-user",
            ApiSecret = "sandbox-secret",
        });

        using var provider = new ShiprocketShippingProvider(
            new SingleHandlerHttpClientFactory(handler),
            options,
            NullLogger<ShiprocketShippingProvider>.Instance);

        var result = await provider.CancelShipmentAsync(providerShipmentId: null, "AWB000001", Cancellation);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        Assert.Equal(4, handler.RequestsSeen);
        Assert.Equal(2, handler.RequestsTo("/v1/external/auth/login"));
        Assert.Equal(2, handler.RequestsTo("/v1/external/orders/cancel"));

        // Both cancel attempts carried a bearer token, and the second carried the refreshed one.
        Assert.Equal(["T1", "T2"], handler.BearerTokensSentTo("/v1/external/orders/cancel"));
    }

    /// <summary>A second, immediate 401 after the one retry is reported as a failure, not looped.</summary>
    [Fact]
    public async Task A_second_401_after_the_retry_is_reported_rather_than_looped()
    {
        var handler = new ScriptedHandler();

        handler.Enqueue(HttpMethod.Post, "/v1/external/auth/login", HttpStatusCode.OK, """{"token":"T1"}""");
        handler.Enqueue(HttpMethod.Post, "/v1/external/orders/cancel", HttpStatusCode.Unauthorized, "{}");
        handler.Enqueue(HttpMethod.Post, "/v1/external/auth/login", HttpStatusCode.OK, """{"token":"T2"}""");
        handler.Enqueue(HttpMethod.Post, "/v1/external/orders/cancel", HttpStatusCode.Unauthorized, "{}");

        var options = new FixedShippingOptions(new ShippingOptions
        {
            BaseUrl = "https://apiv2.shiprocket.in",
            Provider = "shiprocket",
            ApiUser = "sandbox-user",
            ApiSecret = "sandbox-secret",
        });

        using var provider = new ShiprocketShippingProvider(
            new SingleHandlerHttpClientFactory(handler),
            options,
            NullLogger<ShiprocketShippingProvider>.Instance);

        var result = await provider.CancelShipmentAsync(providerShipmentId: null, "AWB000002", Cancellation);

        Assert.True(result.IsFailure);
        Assert.Equal(4, handler.RequestsSeen);
        Assert.Equal(2, handler.RequestsTo("/v1/external/auth/login"));
    }

    /// <summary>An <see cref="IOptionsMonitor{T}"/> stub carrying one fixed <see cref="ShippingOptions"/>.</summary>
    private sealed class FixedShippingOptions(ShippingOptions value) : IOptionsMonitor<ShippingOptions>
    {
        public ShippingOptions CurrentValue { get; } = value;

        public ShippingOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<ShippingOptions, string?> listener) => null;
    }

    /// <summary>An <see cref="IHttpClientFactory"/> that always hands back the one scripted client.</summary>
    private sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>A queue of scripted responses, keyed by method and path, recording what it saw.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpMethod Method, string Path, HttpStatusCode Status, string Body)> _script = new();
        private readonly List<(string Path, string? Bearer)> _seen = [];

        public int RequestsSeen => _seen.Count;

        public void Enqueue(HttpMethod method, string path, HttpStatusCode status, string body)
            => _script.Enqueue((method, path, status, body));

        public int RequestsTo(string path) => _seen.Count(entry => entry.Path == path);

        public IReadOnlyList<string?> BearerTokensSentTo(string path)
            => [.. _seen.Where(entry => entry.Path == path).Select(entry => entry.Bearer)];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            _seen.Add((path, request.Headers.Authorization?.Parameter));

            if (_script.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            var (method, expectedPath, status, body) = _script.Dequeue();

            Assert.Equal(method, request.Method);
            Assert.Equal(expectedPath, path);

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
