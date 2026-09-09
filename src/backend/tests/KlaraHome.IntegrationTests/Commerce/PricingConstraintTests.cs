using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Identity.Infrastructure.Seeding;
using KlaraHome.Modules.Pricing.Endpoints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The guarantees the <c>pricing</c> schema itself makes, plus the two rules a query filter cannot
/// express: what a vendor caller may read and what they may write.
/// </summary>
/// <remarks>
/// A validator is a policy and a constraint is a guarantee. Everything here that can be asked
/// through the API is asked through the API first, because that is the message an operator reads,
/// and then behind it with a direct statement, because the schema is what holds when a row arrives
/// from a restore, a migration or a future import that nobody has written yet.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class PricingConstraintTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// The <c>pricing</c> schema is applied, and every <c>CHECK</c> in it refuses what it is meant
    /// to.
    /// </summary>
    /// <remarks>
    /// The money constraints are the ones that matter: a negative price, a quantity tier below one,
    /// a rate above a hundred per cent, a wallet below zero and a movement of nothing. Every one of
    /// them is a figure that would go on an invoice, and a validator that was skipped — by an import,
    /// by a script, by a handler somebody adds next year — is not a defence.
    /// </remarks>
    [Fact]
    public async Task Every_check_constraint_refuses_what_it_is_meant_to()
    {
        SkipWithoutDocker();

        Factory.Features[Modules.Pricing.Infrastructure.PricingFeatureFlags.StoreCredit] = true;

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var pricing = new PricingScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();
        var offer = await pricing.OfferAsync(catalogue, taxonomy, seller.Id, PricingScenario.NewHsn(), 18m, 1000.00m);

        // All seven tables are here, which is the schema half of the row: the migration applied.
        var tables = await Database.RowsAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'pricing' ORDER BY table_name",
            Cancellation);

        Assert.Equal(
            [
                "price_list_items",
                "price_lists",
                "promotion_redemptions",
                "promotions",
                "tax_rates",
                "wallet_transactions",
                "wallets",
            ],
            tables.Select(row => (string)row["table_name"]!).Where(name => !name.StartsWith("__", StringComparison.Ordinal)));

        var list = await pricing.PriceListAsync(20, startsAt: DateTimeOffset.UtcNow.AddDays(-1));

        await ReadAsync(await pricing.SetPricesAsync(list, [(offer.ListingId, 800.00m, 1)]));

        var itemId = await Database.ScalarAsync<Guid>(
            "SELECT id FROM pricing.price_list_items WHERE price_list_id = $1 LIMIT 1",
            Cancellation,
            list);

        // Through the API first, because that is the message an operator reads.
        await RefusedAsync(
            await pricing.SetPricesAsync(list, [(offer.ListingId, -1.00m, 1)]),
            HttpStatusCode.UnprocessableEntity);

        await RefusedAsync(
            await pricing.SetPricesAsync(list, [(offer.ListingId, 100.00m, 0)]),
            HttpStatusCode.UnprocessableEntity);

        await RefusedAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/price-lists",
                new
                {
                    vendorId = (Guid?)null,
                    code = $"BAD-{Guid.NewGuid():N}"[..12],
                    name = "Closes before it opens",
                    type = "Sale",
                    priority = 10,
                    startsAt = DateTimeOffset.UtcNow.AddDays(5),
                    endsAt = DateTimeOffset.UtcNow.AddDays(1),
                },
                Cancellation),
            HttpStatusCode.UnprocessableEntity);

        await RefusedAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/tax-rates",
                new
                {
                    hsnCode = "not-a-code",
                    description = (string?)null,
                    rate = 18m,
                    cessRate = 0m,
                    effectiveFrom = new DateOnly(2024, 1, 1),
                    effectiveTo = (DateOnly?)null,
                },
                Cancellation),
            HttpStatusCode.UnprocessableEntity);

        var rateId = await pricing.TaxRateAsync(PricingScenario.NewHsn(), 18m);

        var shopper = CreateClient();
        var customerId = await PricingScenario.ShopperAsync(shopper, Cancellation);

        await RunOnceAsync<IStoreCredit>(async (credit, cancellation) =>
            await credit.CreditAsync(
                customerId,
                100m,
                StoreCreditReasons.Refund,
                "return",
                Guid.CreateVersion7(),
                cancellationToken: cancellation));

        var promotion = await pricing.PromotionAsync(
            code: PricingScenario.NewCouponCode(),
            scope: new { listingIds = new[] { offer.ListingId } });

        // And behind it: every one of these is a statement the schema refuses on its own.
        (string Constraint, string Statement, object?[] Parameters)[] refusals =
        [
            ("a negative price",
                "UPDATE pricing.price_list_items SET price_amount = -1 WHERE id = $1", [itemId]),
            ("a quantity tier below one",
                "UPDATE pricing.price_list_items SET min_quantity = 0 WHERE id = $1", [itemId]),
            ("a price list that closes before it opens",
                "UPDATE pricing.price_lists SET ends_at = starts_at - interval '1 day' WHERE id = $1", [list]),
            ("a price list ranked past the ceiling",
                "UPDATE pricing.price_lists SET priority = 1001 WHERE id = $1", [list]),
            ("a GST rate above one hundred per cent",
                "UPDATE pricing.tax_rates SET rate = 101 WHERE id = $1", [rateId]),
            ("a cess rate above one hundred per cent",
                "UPDATE pricing.tax_rates SET cess_rate = 101 WHERE id = $1", [rateId]),
            ("a rate window that ends before it starts",
                "UPDATE pricing.tax_rates SET effective_to = effective_from - 1 WHERE id = $1", [rateId]),
            ("an HSN code that is not four to eight digits",
                "UPDATE pricing.tax_rates SET hsn_code = '12A4' WHERE id = $1", [rateId]),
            ("a negative wallet balance",
                "UPDATE pricing.wallets SET balance_amount = -1 WHERE customer_id = $1", [customerId]),
            ("a wallet movement of nothing",
                "UPDATE pricing.wallet_transactions SET amount_amount = 0 "
                + "WHERE wallet_id IN (SELECT id FROM pricing.wallets WHERE customer_id = $1)", [customerId]),
            ("a wallet movement leaving a negative balance",
                "UPDATE pricing.wallet_transactions SET balance_after_amount = -1 "
                + "WHERE wallet_id IN (SELECT id FROM pricing.wallets WHERE customer_id = $1)", [customerId]),
            ("a negative promotion value",
                "UPDATE pricing.promotions SET value = -1 WHERE id = $1", [promotion]),
            ("a usage counter driven below zero",
                "UPDATE pricing.promotions SET usage_count = -1 WHERE id = $1", [promotion]),
            ("a usage limit of nothing",
                "UPDATE pricing.promotions SET usage_limit_total = 0 WHERE id = $1", [promotion]),
            ("a promotion that closes before it opens",
                "UPDATE pricing.promotions SET ends_at = starts_at - interval '1 day' WHERE id = $1", [promotion]),
            ("a discount ceiling of nothing",
                "UPDATE pricing.promotions SET max_discount = 0 WHERE id = $1", [promotion]),
            ("a promotion type nothing computes",
                "UPDATE pricing.promotions SET type = 'Whatever' WHERE id = $1", [promotion]),
            ("a wallet movement of an unknown kind",
                "UPDATE pricing.wallet_transactions SET type = 'Whatever' "
                + "WHERE wallet_id IN (SELECT id FROM pricing.wallets WHERE customer_id = $1)", [customerId]),
        ];

        foreach (var (constraint, statement, parameters) in refusals)
        {
            var sqlState = await Database.RefusalAsync(statement, Cancellation, parameters);

            Assert.True(
                sqlState == "23514",
                $"The database accepted {constraint}; it answered {sqlState ?? "nothing at all"} instead of 23514.");
        }
    }

    /// <summary>
    /// The two partial unique indexes constrain the rows they name and leave the rest alone: any
    /// number of automatic rules with no code, and any number of wallet movements with no reference.
    /// </summary>
    /// <remarks>
    /// A partial index is the only way to say both halves at once. Without the filter, a store could
    /// have exactly one automatic cart rule and exactly one manual wallet adjustment per customer —
    /// both of which are things a marketplace does constantly — and with no index at all the same
    /// coupon code could exist twice and the same refund could be credited twice.
    /// </remarks>
    [Fact]
    public async Task The_partial_unique_indexes_constrain_only_the_rows_they_are_meant_to()
    {
        SkipWithoutDocker();

        Factory.Features[Modules.Pricing.Infrastructure.PricingFeatureFlags.StoreCredit] = true;

        var admin = await SignedInAdministratorAsync();
        var pricing = new PricingScenario(admin, Cancellation);

        var elsewhere = new[] { Guid.CreateVersion7() };

        // Three automatic rules, none of them carrying a code. The filter is what permits this.
        var automatic = new List<Guid>();

        for (var index = 0; index < 3; index++)
        {
            automatic.Add(await pricing.PromotionAsync(
                priority: 200 + index,
                scope: new { listingIds = elsewhere },
                activate: false));
        }

        Assert.Equal(3, automatic.Distinct().Count());

        var taken = await pricing.PromotionAsync(
            code: PricingScenario.NewCouponCode(),
            scope: new { listingIds = elsewhere },
            activate: false);

        var code = await CodeOfAsync(admin, taken);

        // The handler refuses a second promotion with that code…
        await RefusedAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/promotions",
                new
                {
                    code,
                    name = "The same code again",
                    description = (string?)null,
                    type = "Percentage",
                    appliesTo = "Order",
                    value = 5m,
                    scope = (object?)null,
                    conditions = (object?)null,
                    stacking = "Exclusive",
                    priority = 100,
                    startsAt = DateTimeOffset.UtcNow.AddDays(-1),
                    endsAt = (DateTimeOffset?)null,
                    usageLimitTotal = (int?)null,
                    usageLimitPerCustomer = (int?)null,
                    minOrderValue = 0m,
                    maxDiscount = (decimal?)null,
                },
                Cancellation),
            HttpStatusCode.Conflict,
            "PRICING_DUPLICATE");

        // …and the index refuses it behind the handler's back, which is the guarantee.
        Assert.Equal(
            "23505",
            await Database.RefusalAsync(
                "UPDATE pricing.promotions SET code = $2 WHERE id = $1",
                Cancellation,
                automatic[0],
                code));

        // A second row with no code at all is fine, because the filter excludes it.
        Assert.Null(
            await Database.RefusalAsync(
                "UPDATE pricing.promotions SET name = name || ' (touched)' WHERE id = $1",
                Cancellation,
                automatic[1]));

        var shopper = CreateClient();
        var customerId = await PricingScenario.ShopperAsync(shopper, Cancellation);

        var referenced = Guid.CreateVersion7();

        await RunOnceAsync<IStoreCredit>(async (credit, cancellation) =>
        {
            // One movement carrying a reference, and two carrying none.
            await credit.CreditAsync(
                customerId,
                100m,
                StoreCreditReasons.Refund,
                "return",
                referenced,
                cancellationToken: cancellation);
        });

        var walletId = await Database.ScalarAsync<Guid>(
            "SELECT id FROM pricing.wallets WHERE customer_id = $1",
            Cancellation,
            customerId);

        for (var index = 0; index < 2; index++)
        {
            await ReadAsync(await admin.PostAsJsonAsync(
                $"/api/v1/admin/wallets/{customerId}/adjust",
                new
                {
                    amount = 25m,
                    reason = StoreCreditReasons.Adjustment,
                    note = "By hand.",
                    expiresAt = (DateTimeOffset?)null,
                },
                Cancellation));
        }

        // Two hand adjustments, both with a null reference, both stored: the filter is what allows a
        // support desk to make a second goodwill gesture to the same customer.
        Assert.Equal(
            2,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM pricing.wallet_transactions "
                + "WHERE wallet_id = $1 AND reference_id IS NULL",
                Cancellation,
                walletId));

        var unreferenced = await Database.ScalarAsync<Guid>(
            "SELECT id FROM pricing.wallet_transactions "
            + "WHERE wallet_id = $1 AND reference_id IS NULL LIMIT 1",
            Cancellation,
            walletId);

        // Give one of them the reference the credit already used, and the index refuses it.
        Assert.Equal(
            "23505",
            await Database.RefusalAsync(
                "UPDATE pricing.wallet_transactions "
                + "SET reference_type = 'return', reference_id = $2, type = 'Credit' WHERE id = $1",
                Cancellation,
                unreferenced,
                referenced));
    }

    /// <summary>
    /// A promotion's scope and conditions round-trip through <c>jsonb</c> intact, tier ladder
    /// included.
    /// </summary>
    /// <remarks>
    /// The tier ladder is a collection of complex values inside a JSON document, which EF will not
    /// infer on its own — without being told, it reads the list as a relationship to a table that
    /// does not exist. So "it saved and it came back" is a real question here rather than a
    /// formality, and the answer is money: a ladder that lost its steps is a campaign that quietly
    /// discounts nothing.
    /// </remarks>
    [Fact]
    public async Task A_promotions_scope_and_conditions_round_trip_through_jsonb_intact()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var pricing = new PricingScenario(admin, Cancellation);

        var categories = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var brands = new[] { Guid.CreateVersion7() };
        var vendors = new[] { Guid.CreateVersion7() };
        var listings = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var excluded = new[] { Guid.CreateVersion7() };
        var bundle = new[] { listings[0], listings[1] };

        var promotionId = await pricing.PromotionAsync(
            type: "Tiered",
            appliesTo: "Order",
            value: 0m,
            code: PricingScenario.NewCouponCode(),
            activate: false,
            scope: new
            {
                categoryIds = categories,
                brandIds = brands,
                vendorIds = vendors,
                listingIds = listings,
                excludedListingIds = excluded,
                segments = new[] { "vip", "early-access" },
            },
            conditions: new
            {
                minQuantity = 3,
                firstOrderOnly = true,
                paymentMethods = new[] { "CashOnDelivery" },
                maxQuantityPerOrder = 4,
                buyQuantity = 2,
                getQuantity = 1,
                getDiscountPercent = 50m,
                bundleListingIds = bundle,
                bundlePrice = 1499.50m,
                tiers = new[]
                {
                    new { minAmount = 1000m, value = 5m },
                    new { minAmount = 5000m, value = 10m },
                    new { minAmount = 20_000m, value = 15m },
                },
                tiersArePercentage = false,
            });

        var reloaded = await ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/promotions/{promotionId}", UriKind.Relative), Cancellation));

        var scope = reloaded.GetProperty("scope");
        var conditions = reloaded.GetProperty("conditions");

        Assert.Equal(categories, Guids(scope, "categoryIds"));
        Assert.Equal(brands, Guids(scope, "brandIds"));
        Assert.Equal(vendors, Guids(scope, "vendorIds"));
        Assert.Equal(listings, Guids(scope, "listingIds"));
        Assert.Equal(excluded, Guids(scope, "excludedListingIds"));
        Assert.Equal(
            ["vip", "early-access"],
            scope.GetProperty("segments").EnumerateArray().Select(segment => segment.GetString()));

        Assert.Equal(3, conditions.GetProperty("minQuantity").GetInt32());
        Assert.True(conditions.GetProperty("firstOrderOnly").GetBoolean());
        Assert.Equal(
            ["CashOnDelivery"],
            conditions.GetProperty("paymentMethods").EnumerateArray().Select(method => method.GetString()));
        Assert.Equal(4, conditions.GetProperty("maxQuantityPerOrder").GetInt32());
        Assert.Equal(2, conditions.GetProperty("buyQuantity").GetInt32());
        Assert.Equal(1, conditions.GetProperty("getQuantity").GetInt32());
        Assert.Equal(50m, PricingScenario.Amount(conditions, "getDiscountPercent"));
        Assert.Equal(bundle, Guids(conditions, "bundleListingIds"));
        Assert.Equal(1499.50m, PricingScenario.Amount(conditions, "bundlePrice"));
        Assert.False(conditions.GetProperty("tiersArePercentage").GetBoolean());

        // The nested ladder, in order, with both numbers of every step.
        var tiers = conditions.GetProperty("tiers").EnumerateArray().ToList();

        Assert.Equal(3, tiers.Count);
        Assert.Equal([1000m, 5000m, 20_000m], tiers.Select(tier => PricingScenario.Amount(tier, "minAmount")));
        Assert.Equal([5m, 10m, 15m], tiers.Select(tier => PricingScenario.Amount(tier, "value")));

        // And the columns really are jsonb documents rather than text that happens to parse.
        var columns = await Database.RowsAsync(
            "SELECT column_name, data_type FROM information_schema.columns "
            + "WHERE table_schema = 'pricing' AND table_name = 'promotions' "
            + "AND column_name IN ('scope', 'conditions') ORDER BY column_name",
            Cancellation);

        Assert.Equal(2, columns.Count);
        Assert.All(columns, column => Assert.Equal("jsonb", column["data_type"]));

        // Read back out of the document itself, so this is the stored ladder and not a projection.
        Assert.Equal(
            3,
            await Database.ScalarAsync<int>(
                "SELECT jsonb_array_length(conditions -> 'Tiers') FROM pricing.promotions WHERE id = $1",
                Cancellation,
                promotionId));
    }

    /// <summary>
    /// A vendor caller reads their own price lists and the platform's and nobody else's, and may
    /// write only to their own.
    /// </summary>
    /// <remarks>
    /// The two halves are different mechanisms and neither implies the other. What a seller can
    /// <em>see</em> is a global query filter, and it has to show the platform's lists because a
    /// platform-wide list prices the seller's own offers — a seller who could not read it could not
    /// be told why their offer is selling at a figure they did not set. What they may
    /// <em>write</em> is <c>PricingScope.CanWrite</c>, and it exists precisely to refuse the write
    /// to a row the filter has just let them read.
    /// </remarks>
    [Fact]
    public async Task A_vendor_reads_their_own_price_lists_and_the_platforms_and_writes_only_to_their_own()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var pricing = new PricingScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var mine = await sellers.ActiveAsync();
        var theirs = await sellers.ActiveAsync();

        var offer = await pricing.OfferAsync(catalogue, taxonomy, mine.Id, PricingScenario.NewHsn(), 18m, 1000.00m);

        var platformWide = await pricing.PriceListAsync(10);
        var ownList = await pricing.PriceListAsync(20, mine.Id);
        var rivalList = await pricing.PriceListAsync(30, theirs.Id);

        await ReadAsync(await pricing.SetPricesAsync(platformWide, [(offer.ListingId, 850.00m, 1)]));

        var (owner, _) = await SignedInVendorOwnerAsync(admin, mine.Id);

        using (owner)
        {
            var visible = await ReadAsync(
                await owner.GetAsync(new Uri("/api/v1/admin/price-lists?size=100", UriKind.Relative), Cancellation));

            var ids = visible.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid())
                .ToHashSet();

            Assert.Contains(ownList, ids);
            Assert.Contains(platformWide, ids);
            Assert.DoesNotContain(rivalList, ids);

            // A competitor's list is not there to be read one at a time either, and the answer is
            // 404 rather than a refusal — a refusal would confirm it exists.
            await RefusedAsync(
                await owner.GetAsync(new Uri($"/api/v1/admin/price-lists/{rivalList}", UriKind.Relative), Cancellation),
                HttpStatusCode.NotFound,
                "PRICING_NOT_FOUND");

            // The platform's list reads, and that is what makes the write rule reachable at all.
            var platformList = await ReadAsync(
                await owner.GetAsync(
                    new Uri($"/api/v1/admin/price-lists/{platformWide}", UriKind.Relative),
                    Cancellation));

            Assert.Equal(JsonValueKind.Null, platformList.GetProperty("vendorId").ValueKind);

            // …and it is refused for writing, which is the rule a query filter cannot express.
            await RefusedAsync(
                await owner.PutAsJsonAsync(
                    $"/api/v1/admin/price-lists/{platformWide}",
                    new
                    {
                        name = "Mine now",
                        type = "Sale",
                        priority = 1,
                        startsAt = (DateTimeOffset?)null,
                        endsAt = (DateTimeOffset?)null,
                    },
                    Cancellation),
                HttpStatusCode.UnprocessableEntity,
                "PRICING_SCOPE");

            await RefusedAsync(
                await pricing.SetPricesAsync(platformWide, [(offer.ListingId, 1.00m, 1)], owner),
                HttpStatusCode.UnprocessableEntity,
                "PRICING_SCOPE");

            await RefusedAsync(
                await owner.DeleteAsync(
                    new Uri($"/api/v1/admin/price-lists/{platformWide}", UriKind.Relative),
                    Cancellation),
                HttpStatusCode.UnprocessableEntity,
                "PRICING_SCOPE");

            // Their own list is theirs to write.
            await ReadAsync(await pricing.SetPricesAsync(ownList, [(offer.ListingId, 900.00m, 1)], owner));

            // And a list they open belongs to them whatever the body says, because the owner comes
            // off the token rather than out of the request.
            var opened = await ReadAsync(
                await owner.PostAsJsonAsync(
                    "/api/v1/admin/price-lists",
                    new
                    {
                        vendorId = theirs.Id,
                        code = $"OWN-{Guid.NewGuid():N}"[..12],
                        name = "Opened by a seller",
                        type = "Sale",
                        priority = 40,
                        startsAt = (DateTimeOffset?)null,
                        endsAt = (DateTimeOffset?)null,
                    },
                    Cancellation));

            Assert.Equal(mine.Id, opened.GetProperty("vendorId").GetGuid());

            // The platform's price is what the seller is shown for their own offer, which is the
            // whole reason the platform's lists have to be readable by them.
            var explained = await ReadAsync(
                await owner.GetAsync(
                    new Uri($"/api/v1/admin/prices/resolve?listingId={offer.ListingId}&quantity=1", UriKind.Relative),
                    Cancellation));

            Assert.Equal(850.00m, PricingScenario.Amount(explained, "unitPrice"));
            Assert.Equal(platformWide, explained.GetProperty("priceListId").GetGuid());
        }
    }

    /// <summary>
    /// Every permission the Pricing endpoints declare is one the catalogue declares, and every one
    /// the module names is actually asked for by an endpoint.
    /// </summary>
    /// <remarks>
    /// The module may not reference Identity, so the two lists are copies and copies drift. A code
    /// an endpoint asks for that the catalogue does not declare can never be granted by any role,
    /// which reads as "the administrator has no permissions" rather than as a missing constant; a
    /// code the catalogue declares that nothing asks for is a checkbox on the roles screen that does
    /// nothing. Both directions are asserted, because they fail differently and both were diffed by
    /// hand at the step boundary.
    /// </remarks>
    [Fact]
    public void Every_permission_the_pricing_endpoints_declare_exists_in_the_catalogue()
    {
        SkipWithoutDocker();

        string[] declared =
        [
            PricingPermissions.PriceListRead,
            PricingPermissions.PriceListManage,
            PricingPermissions.TaxRateRead,
            PricingPermissions.TaxRateManage,
            PricingPermissions.PromotionRead,
            PricingPermissions.PromotionManage,
            PricingPermissions.WalletRead,
            PricingPermissions.WalletAdjust,
        ];

        var required = Factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.Metadata.GetMetadata<RequiredPermissionMetadata>()?.Permission)
            .Where(permission => permission?.StartsWith("pricing.", StringComparison.Ordinal) == true)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var catalogued = PermissionCatalog.All
            .Select(permission => permission.Code)
            .Where(code => code.StartsWith("pricing.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Every code an endpoint asks for is in the catalogue, so a role can grant it.
        Assert.Equal(declared.Order(StringComparer.Ordinal), required);

        // And the catalogue names exactly those and nothing else, so no role screen offers a
        // permission that guards nothing.
        Assert.Equal(declared.Order(StringComparer.Ordinal), catalogued);
    }

    /// <summary>
    /// <c>PriceChanged</c> reaches the outbox in the transaction that wrote the price, fires only
    /// for the base quantity tier, and is not raised when the price did not actually move.
    /// </summary>
    /// <remarks>
    /// Search reprojects from it and the price-drop alerts fire from it, so a false one emails every
    /// subscriber about a sale that never opened and a missing one leaves the storefront showing
    /// yesterday's figure. A bulk tier changing is not a price drop — nobody's alert was about the
    /// five-unit price — and announcing it would train subscribers to ignore the one that matters.
    /// </remarks>
    [Fact]
    public async Task A_price_change_reaches_the_outbox_only_for_the_base_tier_and_only_when_it_moved()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var pricing = new PricingScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();
        var offer = await pricing.OfferAsync(catalogue, taxonomy, seller.Id, PricingScenario.NewHsn(), 18m, 1000.00m);

        var list = await pricing.PriceListAsync(10);

        Assert.Equal(0, await AnnouncementsAsync(offer.ListingId));

        // A price where there was none is a change, and it is announced.
        await ReadAsync(await pricing.SetPricesAsync(list, [(offer.ListingId, 900.00m, 1)]));

        Assert.Equal(1, await AnnouncementsAsync(offer.ListingId));

        // A bulk tier is not: nobody's price-drop alert was about the five-unit price.
        await ReadAsync(await pricing.SetPricesAsync(list, [(offer.ListingId, 750.00m, 5)]));

        Assert.Equal(1, await AnnouncementsAsync(offer.ListingId));

        // Restating the same figure is not a change either, however many times it is sent.
        await ReadAsync(await pricing.SetPricesAsync(list, [(offer.ListingId, 900.00m, 1)]));
        await ReadAsync(await pricing.SetPricesAsync(list, [(offer.ListingId, 900.00m, 1)]));

        Assert.Equal(1, await AnnouncementsAsync(offer.ListingId));

        // Moving it is, and the event carries what it was as well as what it is — "it dropped" is
        // not something a consumer could say from one number.
        await ReadAsync(await pricing.SetPricesAsync(list, [(offer.ListingId, 850.00m, 1)]));

        Assert.Equal(2, await AnnouncementsAsync(offer.ListingId));

        var announcements = await Database.RowsAsync(
            "SELECT payload::text AS body FROM platform.outbox_messages "
            + "WHERE type LIKE $1 AND payload::text LIKE $2 ORDER BY occurred_at",
            Cancellation,
            $"%{nameof(PriceChanged)}%",
            $"%{offer.ListingId}%");

        Assert.Contains("900", (string)announcements[^1]["body"]!, StringComparison.Ordinal);
        Assert.Contains("850", (string)announcements[^1]["body"]!, StringComparison.Ordinal);

        // A write that is refused announces nothing, because the event is enqueued into the same
        // change tracker the price is and the save that would have carried it never happened.
        await RefusedAsync(
            await pricing.SetPricesAsync(list, [(Guid.CreateVersion7(), 500.00m, 1)]),
            HttpStatusCode.UnprocessableEntity,
            "PRICING_UNKNOWN_LISTING");

        Assert.Equal(2, await AnnouncementsAsync(offer.ListingId));

        // Switching the list off changes what every offer in it costs, and that is announced too —
        // a price that changed because an operator flipped a switch is still a price that changed.
        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/price-lists/{list}/deactivate",
            new { },
            Cancellation));

        Assert.Equal(3, await AnnouncementsAsync(offer.ListingId));
    }

    /// <summary>How many price announcements the outbox holds about one offer.</summary>
    /// <param name="listingId">The offer.</param>
    private async Task<long> AnnouncementsAsync(Guid listingId)
        => await Database.CountAsync(
            "SELECT COUNT(*) FROM platform.outbox_messages WHERE type LIKE $1 AND payload::text LIKE $2",
            Cancellation,
            $"%{nameof(PriceChanged)}%",
            $"%{listingId}%");

    /// <summary>Reads a list of identifiers off a JSON object.</summary>
    /// <param name="element">The object.</param>
    /// <param name="name">The property.</param>
    private static IEnumerable<Guid> Guids(JsonElement element, string name)
        => element.GetProperty(name).EnumerateArray().Select(id => id.GetGuid());

    /// <summary>The code a promotion was created with, read back from the API that stored it.</summary>
    /// <param name="admin">A client signed in as platform staff.</param>
    /// <param name="promotionId">The promotion.</param>
    private static async Task<string> CodeOfAsync(HttpClient admin, Guid promotionId)
    {
        var promotion = await ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/promotions/{promotionId}", UriKind.Relative), Cancellation));

        return promotion.GetProperty("code").GetString()!;
    }
}
