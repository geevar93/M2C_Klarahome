using KlaraHome.IntegrationTests.Database;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 16A's <c>DeliveryCoverageSettings</c>, enforced at all five gates and editable without a
/// deploy (ADR-018).
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class DeliveryCoverageTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    private const string SectionKey = "delivery-coverage";

    /// <summary>
    /// A PIN code outside the delivery area is refused with <c>DELIVERY_AREA_NOT_COVERED</c> at the
    /// anonymous storefront check and at checkout address selection — two of the five gates ADR-018
    /// names, and the two a shopper meets before an account holds any state at all.
    /// </summary>
    [Fact]
    public async Task An_uncovered_pincode_is_refused_at_the_storefront_check_and_at_checkout_address_selection()
    {
        SkipWithoutDocker();

        const string uncovered = "560001"; // Bengaluru — outside the default Hyderabad-only policy.

        var storefront = await Rest.ReadAsync(
            await CreateClient().GetAsync(
                new Uri($"/api/v1/store/shipping/serviceability/{uncovered}", UriKind.Relative),
                Cancellation),
            Cancellation);

        Assert.False(storefront.GetProperty("deliverable").GetBoolean());
        Assert.False(storefront.GetProperty("covered").GetBoolean());
        Assert.Equal("DELIVERY_AREA_NOT_COVERED", storefront.GetProperty("reason").GetString());
        Assert.False(string.IsNullOrWhiteSpace(storefront.GetProperty("message").GetString()));

        var admin = await SignedInAdministratorAsync();
        var orders = new OrderScenario(admin, Cancellation);

        var taxonomy = await orders.TaxonomyAsync();
        var seller = await orders.SellerAsync(taxonomy);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await orders.ShopperAsync(client, uncovered);
        await orders.AddToCartAsync(shopper, seller.ListingId);

        await orders.EnsureCashOnDeliveryAsync();

        var session = await Rest.ReadAsync(
            await client.PostAsJsonAsync("/api/v1/store/checkout", new { }, Cancellation),
            Cancellation);

        var sessionId = session.GetProperty("id").GetGuid();

        var refused = await client.PutAsJsonAsync(
            $"/api/v1/store/checkout/{sessionId}/address",
            new { shippingAddressId = shopper.AddressId, billingAddressId = (Guid?)null, gstin = (string?)null },
            Cancellation);

        await RefusedAsync(refused, HttpStatusCode.UnprocessableEntity, "DELIVERY_AREA_NOT_COVERED");
    }

    /// <summary>
    /// A coverage rule tightened while a checkout session is open — after the address was accepted —
    /// refuses the three remaining gates: shipping options quote no service, the basket carries a
    /// blocking issue, and <c>place-order</c> refuses before any stock is held or any money is asked
    /// for.
    /// </summary>
    /// <remarks>
    /// This is the one Step 16A row that money and stock actually ride on: the four gates before
    /// <c>place-order</c> exist to make this case rare, and this one exists to make it safe when it
    /// happens anyway — a rule tightened, or a courier withdrawn, in the seconds a checkout session
    /// was open.
    /// </remarks>
    [Fact]
    public async Task A_coverage_rule_tightened_mid_session_refuses_the_remaining_gates_before_stock_is_held()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var orders = new OrderScenario(admin, Cancellation);

        var taxonomy = await orders.TaxonomyAsync();
        var seller = await orders.SellerAsync(taxonomy);

        var (client, _) = await SignedInShopperAsync();

        // Inside the default coverage — accepted when the address is chosen.
        var shopper = await orders.ShopperAsync(client, "500034");
        await orders.AddToCartAsync(shopper, seller.ListingId);
        await orders.EnsureCashOnDeliveryAsync();

        var before = (await orders.StockLevelsAsync(seller.StockItemId)).Reserved;

        // The whole session, complete — address, a chosen delivery option and a payment method —
        // exactly the state a real checkout is in immediately before a shopper presses pay. Only
        // then is the rule tightened, which is the case this row is about: not an address refused
        // outright, but one accepted and then withdrawn from under an open session.
        var session = await orders.CheckoutAsync(shopper);
        var sessionId = session.GetProperty("id").GetGuid();

        // Tighten the policy now that the whole session has already been accepted: 500034 is no
        // longer covered by anything.
        await SetCoverageAsync(admin, enabled: true, prefixes: ["999"], cities: [], pincodes: []);

        try
        {
            // Gate 4: shipping-option quoting offers nothing for the seller once the destination is
            // no longer covered.
            var options = await client.GetAsync(
                new Uri($"/api/v1/store/checkout/{sessionId}/shipping-options", UriKind.Relative),
                Cancellation);

            var quoted = await Rest.ReadAsync(options, Cancellation);
            var vendorOptions = Assert.Single(quoted.EnumerateArray());
            Assert.Equal(0, vendorOptions.GetProperty("options").GetArrayLength());

            // Gate 3: the basket itself carries a blocking issue naming the address, rendered with
            // the session's own destination pincode — a bare GET /cart carries none, because the
            // pincode only enters the render at checkout. The plain session read is used rather than
            // /review, because /review itself refuses with CART_NOT_READY once a blocking issue
            // exists — it is the last-mile check before payment, not a way to inspect one.
            var checkoutRead = await Rest.ReadAsync(
                await client.GetAsync(
                    new Uri($"/api/v1/store/checkout/{sessionId}", UriKind.Relative),
                    Cancellation),
                Cancellation);

            var cart = checkoutRead.TryGetProperty("cart", out var nested) ? nested : checkoutRead;

            Assert.Contains(
                cart.GetProperty("issues").EnumerateArray(),
                issue => issue.GetProperty("code").GetString() == "DELIVERY_AREA_NOT_COVERED"
                         && issue.GetProperty("isBlocking").GetBoolean());

            // Gate 5: place-order refuses before stock is held or money is asked for.
            var refusal = await orders.PlaceRawAsync(shopper, sessionId);
            await RefusedAsync(refusal, HttpStatusCode.UnprocessableEntity, "DELIVERY_AREA_NOT_COVERED");

            var after = await orders.StockLevelsAsync(seller.StockItemId);
            Assert.Equal(before, after.Reserved);
        }
        finally
        {
            await RestoreDefaultCoverageAsync(admin);
        }
    }

    /// <summary>
    /// Adding <c>560</c> to the allowed prefixes makes Bengaluru orderable with no deploy and no
    /// restart, and disabling coverage restores national trading — both through
    /// <c>PUT /admin/settings/delivery-coverage</c>, and both audited.
    /// </summary>
    [Fact]
    public async Task Editing_the_coverage_settings_takes_effect_immediately_and_is_audited()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        try
        {
            // The admin coverage-test route rather than the anonymous storefront one: the
            // storefront's GET is deliberately output-cached for sixty seconds
            // (`CachePublicRead`) — right for a page a shopper is about to load, wrong for asserting
            // that an edit took effect within the same test. The admin route is the one this step's
            // own acceptance criterion is written against: "would this address be accepted".
            var beforeEdit = await Rest.ReadAsync(
                await admin.GetAsync(
                    new Uri("/api/v1/admin/shipping/coverage/test/560001", UriKind.Relative),
                    Cancellation),
                Cancellation);

            Assert.False(beforeEdit.GetProperty("covered").GetBoolean());

            await SetCoverageAsync(admin, enabled: true, prefixes: ["500", "560"], cities: [], pincodes: []);

            var afterAdd = await Rest.ReadAsync(
                await admin.GetAsync(
                    new Uri("/api/v1/admin/shipping/coverage/test/560001", UriKind.Relative),
                    Cancellation),
                Cancellation);

            Assert.True(afterAdd.GetProperty("covered").GetBoolean());

            var audited = await Database.CountAsync(
                "SELECT COUNT(*) FROM platform.audit_logs WHERE action = 'platform.settings.updated' "
                + "AND entity_id = $1",
                Cancellation,
                SectionKey);

            Assert.True(audited > 0, "No audit entry was written for the delivery-coverage edit.");

            // Disabling coverage entirely restores national trading: even a PIN code matching no
            // rule at all is now deliverable.
            await SetCoverageAsync(admin, enabled: false, prefixes: [], cities: [], pincodes: []);

            var disabled = await Rest.ReadAsync(
                await admin.GetAsync(
                    new Uri("/api/v1/admin/shipping/coverage/test/700001", UriKind.Relative),
                    Cancellation),
                Cancellation);

            Assert.True(disabled.GetProperty("covered").GetBoolean());
        }
        finally
        {
            await RestoreDefaultCoverageAsync(admin);
        }
    }

    /// <summary>
    /// The settings validator refuses an enabled policy with no city, prefix or PIN code at all —
    /// the state that would silently stop the store selling to anybody.
    /// </summary>
    [Fact]
    public async Task The_validator_refuses_an_enabled_policy_with_no_allow_rule_at_all()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/settings/{SectionKey}",
            new
            {
                enabled = true,
                allowedCities = Array.Empty<string>(),
                allowedPincodePrefixes = Array.Empty<string>(),
                allowedPincodes = Array.Empty<string>(),
                blockedPincodes = Array.Empty<string>(),
                message = "We currently deliver within Hyderabad only.",
            },
            Cancellation);

        await RefusedAsync(response, HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// <c>GET /store/config</c> carries the public delivery-coverage summary, so a storefront can say
    /// where the store delivers before a shopper types a PIN code.
    /// </summary>
    [Fact]
    public async Task Store_config_carries_the_public_coverage_summary()
    {
        SkipWithoutDocker();

        Factory.Features["platform.public-store-config"] = true;

        var config = await Rest.ReadAsync(
            await CreateClient().GetAsync(new Uri("/api/v1/store/config", UriKind.Relative), Cancellation),
            Cancellation);

        var section = config.GetProperty("settings").GetProperty(SectionKey);

        Assert.True(section.GetProperty("enabled").GetBoolean());
        Assert.Contains(
            section.GetProperty("allowedCities").EnumerateArray(),
            city => string.Equals(city.GetString(), "Hyderabad", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrWhiteSpace(section.GetProperty("message").GetString()));
    }

    /// <summary>Replaces the delivery-coverage section as a whole, through the admin settings write.</summary>
    private static async Task SetCoverageAsync(
        HttpClient admin,
        bool enabled,
        IReadOnlyList<string> prefixes,
        IReadOnlyList<string> cities,
        IReadOnlyList<string> pincodes)
    {
        var body = new JsonObject
        {
            ["enabled"] = enabled,
            ["allowedCities"] = new JsonArray([.. cities.Select(city => JsonValue.Create(city))]),
            ["allowedPincodePrefixes"] = new JsonArray([.. prefixes.Select(prefix => JsonValue.Create(prefix))]),
            ["allowedPincodes"] = new JsonArray([.. pincodes.Select(pincode => JsonValue.Create(pincode))]),
            ["blockedPincodes"] = new JsonArray(),
            ["message"] = "We currently deliver within Hyderabad only.",
        };

        await Rest.ReadAsync(
            await admin.PutAsync(
                new Uri($"/api/v1/admin/settings/{SectionKey}", UriKind.Relative),
                new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
                Cancellation),
            Cancellation);
    }

    /// <summary>
    /// Puts the delivery-coverage policy back to the shipped default. Tests that edit shared store
    /// settings must restore them — the collection's database is shared by the whole suite.
    /// </summary>
    private static Task RestoreDefaultCoverageAsync(HttpClient admin)
        => SetCoverageAsync(admin, enabled: true, prefixes: ["500"], cities: ["Hyderabad", "Secunderabad"], pincodes: []);
}
