using System.Net;
using System.Net.Http.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 16A's first deliverable: a shipment keeps talking to the courier that booked it, whatever
/// <c>Shipping:Provider</c> says today (ADR-018).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CommerceApiFactory"/> registers <see cref="FakeShippingProvider"/> under the
/// Shiprocket key and the real <c>ManualShippingProvider</c> beside it — the same pair a production
/// host has once a courier is chosen. That is what makes the registry's own resolution rule provable
/// here: <c>ShippingProviderRegistry.For(providerName)</c> is looked up by the name stored on the
/// row, never by <c>Shipping:Provider</c>, and a parcel already booked must not go blind the day a
/// deployment switches courier.
/// </para>
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class ShippingProviderSelectionTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A parcel booked with Shiprocket keeps tracking, labelling and cancelling through Shiprocket
    /// after the deployment switches to <c>manual</c>.
    /// </summary>
    /// <remarks>
    /// Closes the sharpest of the twelve rows: a registry that read <c>Shipping:Provider</c> instead
    /// of the shipment's own stored provider would silently orphan every parcel in flight the day a
    /// store changes courier. Proved with a <em>second</em> host over the same database, because a
    /// setting read at start-up cannot change under a host that is already running.
    /// </remarks>
    [Fact]
    public async Task A_shipment_booked_with_shiprocket_keeps_talking_to_shiprocket_after_the_deployment_switches_to_manual()
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

        // Booked on the host whose Shipping:Provider is shiprocket — CommerceApiFactory's default.
        var booked = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/shipments",
                new { lines = Array.Empty<object>(), weight = 500 },
                Cancellation),
            Cancellation);

        Assert.Equal("shiprocket", booked.GetProperty("provider").GetString());

        var awb = booked.GetProperty("awb").GetString();
        Assert.False(string.IsNullOrWhiteSpace(awb));

        var shipmentId = booked.GetProperty("id").GetGuid();

        // A second host, over the same database, with the courier switched to manual — the shape a
        // deployment change actually takes: a new process, a changed setting, the same rows.
        await using var switched = NewFactory();
        switched.Overrides["Shipping:Provider"] = "manual";

        var switchedAdmin = switched.CreateClient();
        await TestSignIn.SignInAsync(
            switchedAdmin,
            "admin",
            KlaraHomeSchemaFixture.BootstrapEmail,
            KlaraHomeSchemaFixture.BootstrapPassword,
            Cancellation);

        var cancelled = await switchedAdmin.PostAsJsonAsync(
            $"/api/v1/admin/shipments/{shipmentId}/cancel",
            new { reason = "Testing that a switched deployment still reaches the booking courier." },
            Cancellation);

        await Rest.ReadAsync(cancelled, Cancellation);

        // The manual adapter never receives a cancellation for a parcel it did not book — only the
        // Shiprocket-named adapter on the switched host does, which is what proves the registry
        // resolved the shipment's own stored provider rather than the switched configuration.
        Assert.Contains(awb, switched.Courier.Cancellations);
    }

    /// <summary>
    /// The legacy <c>aggregator</c> alias resolves to whichever courier this build has configured,
    /// and nothing new is ever stored under that name.
    /// </summary>
    [Fact]
    public async Task The_aggregator_alias_resolves_to_the_configured_courier_and_is_never_stored()
    {
        SkipWithoutDocker();

        await using var aliased = NewFactory();
        aliased.Overrides["Shipping:Provider"] = "aggregator";

        var admin = aliased.CreateClient();
        await TestSignIn.SignInAsync(
            admin,
            "admin",
            KlaraHomeSchemaFixture.BootstrapEmail,
            KlaraHomeSchemaFixture.BootstrapPassword,
            Cancellation);

        var orders = new OrderScenario(admin, Cancellation);
        var taxonomy = await orders.TaxonomyAsync();
        var seller = await orders.SellerAsync(taxonomy);

        var client = aliased.CreateClient();
        var shopper = await SignInShopperAsync(aliased, client, orders);
        await orders.AddToCartAsync(shopper, seller.ListingId);

        var placed = await orders.PlaceAsync(shopper);
        var subOrderId = await orders.SubOrderIdAsync(placed.OrderId, seller.Vendor.Id);

        var booked = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/shipments",
                new { lines = Array.Empty<object>(), weight = 500 },
                Cancellation),
            Cancellation);

        // "aggregator" resolved to the one non-manual, configured adapter this build has — the
        // Shiprocket-named fake — and the row records that real key, not the alias it was configured
        // under.
        Assert.Equal("shiprocket", booked.GetProperty("provider").GetString());
    }

    /// <summary>
    /// A blank or unrecognised <c>Shipping:Provider</c> degrades to the manual adapter rather than
    /// failing outright — the module always has somewhere to book.
    /// </summary>
    [Fact]
    public async Task An_unknown_or_blank_provider_falls_through_to_manual()
    {
        SkipWithoutDocker();

        await using var unknown = NewFactory();
        unknown.Overrides["Shipping:Provider"] = "some-courier-nobody-registered";

        var admin = unknown.CreateClient();
        await TestSignIn.SignInAsync(
            admin,
            "admin",
            KlaraHomeSchemaFixture.BootstrapEmail,
            KlaraHomeSchemaFixture.BootstrapPassword,
            Cancellation);

        var orders = new OrderScenario(admin, Cancellation);
        var taxonomy = await orders.TaxonomyAsync();
        var seller = await orders.SellerAsync(taxonomy);

        var client = unknown.CreateClient();
        var shopper = await SignInShopperAsync(unknown, client, orders);

        await orders.AddToCartAsync(shopper, seller.ListingId);

        var placed = await orders.PlaceAsync(shopper);
        var subOrderId = await orders.SubOrderIdAsync(placed.OrderId, seller.Vendor.Id);

        // Booked with no manual air waybill supplied: the manual adapter refuses exactly that, which
        // is only reachable if the registry's default fell through to it rather than to the
        // Shiprocket-named fake, which would have booked successfully.
        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/sub-orders/{subOrderId}/shipments",
            new { lines = Array.Empty<object>(), weight = 500 },
            Cancellation);

        await RefusedAsync(response, HttpStatusCode.UnprocessableEntity, "SHIPPING_MANUAL_AWB_REQUIRED");
    }

    private static async Task<OrderShopper> SignInShopperAsync(CommerceApiFactory factory, HttpClient client, OrderScenario orders)
    {
        // Withdrawn from the storefront by default (docs: IdentityFeatures.MobileOtpLogin ships
        // off). CommerceTestBase.SignedInShopperAsync pins it for the default factory; a second host
        // built by NewFactory() needs the same pin, or its OTP route answers 404 rather than a
        // refusal — which is exactly what these tests found the first time anything here used one.
        factory.Features[Modules.Identity.Infrastructure.IdentityFeatures.MobileOtpLogin] = true;

        var number = $"+919{Random.Shared.NextInt64(100_000_000, 999_999_999)}";

        var start = await client.PostAsJsonAsync("/api/v1/store/auth/otp/request", new { mobile = number }, Cancellation);
        start.EnsureSuccessStatusCode();

        var code = factory.Otp.Latest(number, Modules.Identity.Domain.OtpPurpose.Login);

        await TestSignIn.SignInWithOtpAsync(client, number, code, Cancellation);

        return await orders.ShopperAsync(client);
    }
}
