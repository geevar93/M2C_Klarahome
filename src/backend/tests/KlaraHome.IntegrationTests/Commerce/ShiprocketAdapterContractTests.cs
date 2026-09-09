using System.Net;
using System.Net.Http.Json;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Courier.Shiprocket;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// What can be proved about <c>ShiprocketShippingProvider</c> without a live sandbox account
/// (docs/08-integrations.md §2.2, ADR-018).
/// </summary>
/// <remarks>
/// <para>
/// The credentials are deliberately blank — the User's own instruction — so login, token refresh,
/// the two-call booking, the label, the manifest, the pickup and the cancel-and-track calls cannot
/// be proved here: every one of them needs a network Shiprocket answers, and no account exists. That
/// row stays <c>OPEN</c> in <c>TEST_DEBT.md</c>.
/// </para>
/// <para>
/// What does not need a live account is exercised directly against the real adapter code: the
/// <c>x-api-key</c> webhook check, which is a comparison against a configured secret and nothing
/// more; and the outbound host allow-list, which is a check this platform makes on its own request
/// before it ever reaches Shiprocket. Both are constructed by hand, bypassing DI entirely, because
/// <see cref="CommerceApiFactory"/> replaces <c>IShippingProvider</c> with <see cref="FakeShippingProvider"/>
/// for every other test in this suite and the real class is never resolved from that host.
/// </para>
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class ShiprocketAdapterContractTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>A fixed set of <see cref="ShippingOptions"/>, the same shape the dispatcher's own stub uses.</summary>
    private sealed class FixedShippingOptions(ShippingOptions value) : IOptionsMonitor<ShippingOptions>
    {
        public ShippingOptions CurrentValue => value;

        public ShippingOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<ShippingOptions, string?> listener) => null;
    }

    /// <summary>Answers a fixed response without ever touching the network.</summary>
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }

    /// <summary>
    /// Shiprocket's own proof of origin — the shared secret itself, offered in <c>x-api-key</c> —
    /// verifies when it matches the configured secret and refuses when it does not, and the refusal
    /// is a plain mismatch rather than a crash on a different-length header.
    /// </summary>
    [Fact]
    public void VerifyWebhookSignature_accepts_the_configured_x_api_key_and_refuses_a_wrong_one()
    {
        var options = new FixedShippingOptions(new ShippingOptions
        {
            Provider = "shiprocket",
            BaseUrl = "https://apiv2.shiprocket.in",
            WebhookSecret = "the-configured-shared-secret",
        });

        using var provider = new ShiprocketShippingProvider(
            NoOpHttpClientFactory.Instance,
            options,
            NullLogger<ShiprocketShippingProvider>.Instance);

        Assert.True(provider.VerifyWebhookSignature("{\"awb\":\"AWB1\"}", "the-configured-shared-secret"));
        Assert.False(provider.VerifyWebhookSignature("{\"awb\":\"AWB1\"}", "a-completely-wrong-secret"));
        Assert.False(provider.VerifyWebhookSignature("{\"awb\":\"AWB1\"}", "short"));
        Assert.False(provider.VerifyWebhookSignature("{\"awb\":\"AWB1\"}", signature: null));
    }

    /// <summary>
    /// <see cref="ShiprocketWire.MatchesSecret"/> is the primitive the check above is built on: an
    /// exact match verifies, and neither a null nor a blank header is ever treated as a match against
    /// a blank secret.
    /// </summary>
    [Fact]
    public void MatchesSecret_is_an_exact_comparison_that_never_treats_blank_as_a_match()
    {
        Assert.True(ShiprocketWire.MatchesSecret("secret-value", "secret-value"));
        Assert.False(ShiprocketWire.MatchesSecret("secret-value", "different-value"));
        Assert.False(ShiprocketWire.MatchesSecret(null, "secret-value"));
        Assert.False(ShiprocketWire.MatchesSecret(string.Empty, string.Empty));
        Assert.False(ShiprocketWire.MatchesSecret("anything", secret: null));
    }

    /// <summary>
    /// The outbound client refuses any host but the configured one, even once the configured base
    /// URL has been reduced to its origin — so a path pasted into <c>Shipping:BaseUrl</c> cannot move
    /// which endpoints this adapter calls.
    /// </summary>
    [Fact]
    public async Task The_outbound_handler_refuses_any_host_but_the_configured_origin()
    {
        var options = new FixedShippingOptions(new ShippingOptions
        {
            Provider = "shiprocket",
            BaseUrl = "https://apiv2.shiprocket.in/v1/external/",
            WebhookSecret = "secret",
        });

        var handler = new ShiprocketAllowedHostHandler(options, NullLogger<ShiprocketAllowedHostHandler>.Instance)
        {
            InnerHandler = new StubHandler(HttpStatusCode.OK),
        };

        using var client = new HttpClient(handler);

        // The configured host, reached through a path the adapter itself would call. Allowed, even
        // though the setting carries a path segment the request does not.
        var allowed = await client.GetAsync(new Uri("https://apiv2.shiprocket.in/v1/external/open/postcode/details"), Cancellation);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        // Any other host — including one that merely looks similar — is refused before the request
        // leaves this process, whatever path or scheme it asks for.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(
                new Uri("https://apiv2.shiprocket.in.evil.example/v1/external/open/postcode/details"),
                Cancellation));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(new Uri("https://attacker.example/steal"), Cancellation));
    }

    /// <summary>
    /// A Shiprocket webhook carrying the correct key is verified, stored and answered <c>200</c>; a
    /// replay of the exact same event is answered <c>200</c> again but recorded only once; one
    /// carrying the wrong key is stored, marked ignored and answered <c>401</c>.
    /// </summary>
    /// <remarks>
    /// Driven through the real endpoint and the real replay-protection query, with
    /// <see cref="FakeShippingProvider"/> standing in for the network call Shiprocket's own adapter
    /// would otherwise need credentials for — the same substitution boundary
    /// <see cref="CommerceApiFactory"/> draws everywhere else. What is under test here is the
    /// receiver's own behaviour: the signature it is handed, the storage, the deduplication and the
    /// status code, none of which depend on which adapter answered the signature check.
    /// </remarks>
    [Fact]
    public async Task A_shiprocket_webhook_is_verified_stored_and_applied_exactly_once_a_wrong_key_is_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var orders = new OrderScenario(admin, Cancellation);

        var taxonomy = await orders.TaxonomyAsync();
        var seller = await orders.SellerAsync(taxonomy);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await orders.ShopperAsync(client);
        await orders.AddToCartAsync(shopper, seller.ListingId);

        var placed = await orders.PlaceAsync(shopper);
        var subOrderId = await orders.SubOrderIdAsync(placed.OrderId, seller.Vendor.Id);

        var booked = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/shipments",
                new { lines = Array.Empty<object>(), weight = 500 },
                Cancellation),
            Cancellation);

        var awb = booked.GetProperty("awb").GetString()!;

        var scan = Factory.Courier.Scan(awb, ShipmentStatus.PickedUp);
        var body = Factory.Courier.WebhookBody(awb, scan);
        var signature = FakeShippingProvider.Sign(body);

        var first = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/shipping/shiprocket",
            body,
            ("X-Shipping-Signature", signature));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var storedAfterFirst = await Database.CountAsync(
            "SELECT COUNT(*) FROM shipping.courier_events WHERE provider = 'shiprocket' AND provider_event_id = $1",
            Cancellation,
            scan.ProviderEventId);

        Assert.Equal(1, storedAfterFirst);

        // A replay of the exact same signed body — the same event id — is answered 200 again, and
        // is not stored a second time. The unique index is the real guarantee; the endpoint's own
        // pre-check is what turns the second delivery into an ordinary 200 rather than a constraint
        // violation the courier would see as a failure worth retrying.
        var replay = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/shipping/shiprocket",
            body,
            ("X-Shipping-Signature", signature));

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        var storedAfterReplay = await Database.CountAsync(
            "SELECT COUNT(*) FROM shipping.courier_events WHERE provider = 'shiprocket' AND provider_event_id = $1",
            Cancellation,
            scan.ProviderEventId);

        Assert.Equal(1, storedAfterReplay);

        // A different scan (a fresh event id) signed with the wrong secret: stored as evidence,
        // marked ignored, and answered 401 — the one case that is not a plain 200, because somebody
        // sending forged tracking updates is worth a record and worth telling.
        var forgedScan = Factory.Courier.Scan(awb, ShipmentStatus.Delivered);
        var forgedBody = Factory.Courier.WebhookBody(awb, forgedScan);

        var wrongSignature = await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/shipping/shiprocket",
            forgedBody,
            ("X-Shipping-Signature", "not-the-right-signature-at-all"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongSignature.StatusCode);

        var forgedRow = await Database.RowsAsync(
            "SELECT signature_valid, status FROM shipping.courier_events "
            + "WHERE provider = 'shiprocket' AND provider_event_id = $1",
            Cancellation,
            forgedScan.ProviderEventId);

        var row = Assert.Single(forgedRow);
        Assert.False((bool)row["signature_valid"]!);
        Assert.Equal("Ignored", row["status"]!.ToString());
    }

    /// <summary>An <see cref="IHttpClientFactory"/> that is never called on the paths under test here.</summary>
    private sealed class NoOpHttpClientFactory : IHttpClientFactory
    {
        public static readonly NoOpHttpClientFactory Instance = new();

        public HttpClient CreateClient(string name) => new();
    }
}
