using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace KlaraHome.IntegrationTests.Api;

/// <summary>
/// The global backstop from docs/04-api-specification.md §6, with the allowance turned down so
/// the assertion is about behaviour rather than about issuing six hundred requests.
/// </summary>
/// <remarks>
/// Each test gets its own host: a fixed window is shared state, so a shared fixture would make
/// these tests depend on the order they run in.
/// </remarks>
public sealed class RateLimitingTests
{
    private const int PermitLimit = 3;

    [Fact]
    public async Task Requests_within_the_allowance_are_served()
    {
        await using var factory = new LimitedFactory();
        using var client = factory.CreateClient();

        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            using var response = await GetMetaAsync(client);

            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                $"request {attempt} is within the allowance but returned {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Requests_beyond_the_allowance_are_refused_with_429_and_a_problem_document()
    {
        await using var factory = new LimitedFactory();
        using var client = factory.CreateClient();

        await ExhaustAllowanceAsync(client);

        using var refused = await GetMetaAsync(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType!.MediaType);

        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("RATE_LIMITED", problem.GetProperty("code").GetString());
        Assert.Equal(429, problem.GetProperty("status").GetInt32());
        Assert.Equal("https://klarahome.dev/errors/rate-limited", problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_refusal_tells_the_client_the_limit_and_when_to_come_back()
    {
        await using var factory = new LimitedFactory();
        using var client = factory.CreateClient();

        await ExhaustAllowanceAsync(client);

        using var refused = await GetMetaAsync(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.RetryAfter!.Delta!.Value > TimeSpan.Zero);
        Assert.Equal(
            PermitLimit.ToString(CultureInfo.InvariantCulture),
            Assert.Single(refused.Headers.GetValues("X-RateLimit-Limit")));
        Assert.Equal("0", Assert.Single(refused.Headers.GetValues("X-RateLimit-Remaining")));
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(refused.Headers.GetValues("X-RateLimit-Reset"))));
    }

    [Fact]
    public async Task Health_probes_are_never_rate_limited_so_a_burst_cannot_look_like_an_outage()
    {
        await using var factory = new LimitedFactory();
        using var client = factory.CreateClient();

        for (var attempt = 1; attempt <= PermitLimit + 3; attempt++)
        {
            using var response = await client.GetAsync(
                new Uri("/health/live", UriKind.Relative),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private static async Task ExhaustAllowanceAsync(HttpClient client)
    {
        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            (await GetMetaAsync(client)).Dispose();
        }
    }

    private static Task<HttpResponseMessage> GetMetaAsync(HttpClient client)
        => client.GetAsync(new Uri("/api/v1/meta", UriKind.Relative), TestContext.Current.CancellationToken);

    /// <summary>A host whose global allowance is three requests per minute.</summary>
    private sealed class LimitedFactory : KlaraHomeApiFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings["RateLimiting:Enabled"] = "true";
                settings["RateLimiting:Global:PermitLimit"] =
                    PermitLimit.ToString(CultureInfo.InvariantCulture);
                settings["RateLimiting:Global:WindowSeconds"] = "60";
                return settings;
            }
        }
    }
}
