using System.Globalization;
using System.Net;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The GST engine against a live database: the rate that was in force, the place of supply, and the
/// thirty-scenario golden suite that is Step 12's own acceptance criterion.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic itself is unit-tested and is a pure function. What was never proved is everything
/// around it: that the rate comes from the row in force <em>on the date of supply</em> rather than
/// the newest one, that the place of supply is decided per line against that line's seller's own
/// registration, and that the figures a shopper is shown are the figures the engine computed once
/// the catalogue, the rate table and the promotion walk have all had their say.
/// </para>
/// <para>
/// Every HSN code these tests record a rate against is minted fresh. The collection shares one
/// database and one rate table, and a code two tests both wrote to would be two tests disagreeing
/// about what the law says.
/// </para>
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class TaxTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A rate change is a new row, and a supply quoted for a past date still resolves to the rate
    /// that was in force then.
    /// </summary>
    /// <remarks>
    /// This is the whole reason <c>tax_rates</c> is a row per period instead of a column somebody
    /// edits. An invoice reprinted after the Council moves a rate has to reproduce the figures the
    /// customer was charged; a resolver that read "the current row" would silently rewrite history
    /// every time, and the discrepancy would surface at a filing rather than at a code review.
    /// </remarks>
    [Fact]
    public async Task A_rate_change_leaves_a_past_supply_at_the_rate_that_was_in_force()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var pricing = new PricingScenario(admin, Cancellation);

        var hsn = PricingScenario.NewHsn();

        // Twelve per cent until the end of 2024, eighteen from the first day of 2025.
        await pricing.TaxRateAsync(hsn, 12m, effectiveFrom: new DateOnly(2020, 1, 1), effectiveTo: new DateOnly(2024, 12, 31));
        await pricing.TaxRateAsync(hsn, 18m, effectiveFrom: new DateOnly(2025, 1, 1));

        var duringTheOldRate = await ResolveAsync(admin, hsn, new DateOnly(2024, 6, 30));
        var onTheDayItChanged = await ResolveAsync(admin, hsn, new DateOnly(2025, 1, 1));
        var theDayBefore = await ResolveAsync(admin, hsn, new DateOnly(2024, 12, 31));
        var today = await ResolveAsync(admin, hsn, asOf: null);

        Assert.Equal(12m, PricingScenario.Amount(duringTheOldRate, "rate"));
        Assert.Equal(12m, PricingScenario.Amount(theDayBefore, "rate"));
        Assert.Equal(18m, PricingScenario.Amount(onTheDayItChanged, "rate"));
        Assert.Equal(18m, PricingScenario.Amount(today, "rate"));

        // The rows are different rows, which is what makes reprinting possible at all.
        Assert.NotEqual(
            duringTheOldRate.GetProperty("taxRateId").GetGuid(),
            today.GetProperty("taxRateId").GetGuid());

        // Before either window opened there is nothing in force, and that is a 404 rather than a
        // guess: a supply made before the store recorded a rate is a question, not a zero.
        await RefusedAsync(
            await admin.GetAsync(
                new Uri($"/api/v1/admin/tax-rates/resolve?hsnCode={hsn}&asOf=2019-01-01", UriKind.Relative),
                Cancellation),
            HttpStatusCode.NotFound,
            "PRICING_NOT_FOUND");

        // A code with no row at all falls back to the product's own rate rather than failing, which
        // is what lets a store sell before anybody has typed out the HSN schedule.
        await RefusedAsync(
            await admin.GetAsync(
                new Uri($"/api/v1/admin/tax-rates/resolve?hsnCode={PricingScenario.NewHsn()}", UriKind.Relative),
                Cancellation),
            HttpStatusCode.NotFound,
            "PRICING_NOT_FOUND");
    }

    /// <summary>
    /// A basket spanning two GST states produces CGST + SGST on one line and IGST on the other, and
    /// each seller's group totals are the invoice their sub-order will carry.
    /// </summary>
    /// <remarks>
    /// The place of supply is decided per line against <em>that line's seller's</em> registration,
    /// not against the store's, because each seller invoices under their own. A marketplace that
    /// decided it once for the basket would put the wrong tax on one of the two invoices every time
    /// a shopper bought from two states at once — and it is the seller, not the platform, who is
    /// assessed on it.
    /// </remarks>
    [Fact]
    public async Task A_basket_across_two_gst_states_splits_one_line_cgst_sgst_and_the_other_igst()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var pricing = new PricingScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();

        var here = await pricing.SellerRegisteredInAsync(sellers, PricingScenario.TelanganaGstCode);
        var away = await pricing.SellerRegisteredInAsync(sellers, PricingScenario.MaharashtraGstCode);

        var hsn = PricingScenario.NewHsn();
        await pricing.TaxRateAsync(hsn, 18m);

        var local = await pricing.OfferAsync(catalogue, taxonomy, here, hsn, 18m, 2999.00m);
        var distant = await pricing.OfferAsync(catalogue, taxonomy, away, hsn, 18m, 2999.00m);

        var quote = await pricing.QuoteAsync(
            CreateClient(),
            [(local.ListingId, 1), (distant.ListingId, 1)],
            await pricing.StateAsync(PricingScenario.TelanganaGstCode));

        var localLine = PricingScenario.LineFor(quote, local.ListingId);
        var distantLine = PricingScenario.LineFor(quote, distant.ListingId);

        // Same goods, same price, same rate, two different splits — because the two sellers are
        // registered in two different states and only one of them is where the goods are going.
        Assert.Equal(2541.53m, PricingScenario.Amount(localLine, "taxableValue"));
        Assert.Equal(228.74m, PricingScenario.Amount(localLine, "cgst"));
        Assert.Equal(228.73m, PricingScenario.Amount(localLine, "sgst"));
        Assert.Equal(0m, PricingScenario.Amount(localLine, "igst"));

        Assert.Equal(2541.53m, PricingScenario.Amount(distantLine, "taxableValue"));
        Assert.Equal(0m, PricingScenario.Amount(distantLine, "cgst"));
        Assert.Equal(0m, PricingScenario.Amount(distantLine, "sgst"));
        Assert.Equal(457.47m, PricingScenario.Amount(distantLine, "igst"));

        // The odd paisa goes to CGST, so the two halves still sum to the whole.
        Assert.Equal(457.47m, PricingScenario.Amount(localLine, "cgst") + PricingScenario.Amount(localLine, "sgst"));

        // And each seller's group is the invoice their sub-order will carry: their own lines'
        // taxable value and their own lines' tax, not a share of the basket's.
        var localGroup = PricingScenario.GroupFor(quote, here);
        var distantGroup = PricingScenario.GroupFor(quote, away);

        Assert.Equal(2541.53m, PricingScenario.Amount(localGroup, "taxableValue"));
        Assert.Equal(457.47m, PricingScenario.Amount(localGroup, "taxTotal"));
        Assert.Equal(2999.00m, PricingScenario.Amount(localGroup, "total"));

        Assert.Equal(2541.53m, PricingScenario.Amount(distantGroup, "taxableValue"));
        Assert.Equal(457.47m, PricingScenario.Amount(distantGroup, "taxTotal"));
        Assert.Equal(2999.00m, PricingScenario.Amount(distantGroup, "total"));

        // The basket-level flag describes the store's own supply — shipping and the COD fee — and
        // deliberately says nothing about either line, which is why the splits are on the lines.
        Assert.Equal(
            PricingScenario.TelanganaGstCode,
            quote.GetProperty("placeOfSupplyStateCode").GetString());

        AssertReconciles(quote);
    }

    /// <summary>
    /// Thirty pricing and tax scenarios reproduce exactly: the rate sweep intra-state and
    /// inter-state, cess, a rate that falls back to the product's own, coupon-and-tax interaction,
    /// rupee rounding in both directions, and a multi-vendor basket.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is Step 12's stated full acceptance criterion, and it is written as a table of expected
    /// figures rather than as thirty tests because that is what makes it readable as a specification:
    /// every number below was computed from the documented rule — taxable value is
    /// <c>gross / (1 + (rate + cess) / 100)</c>, cess is rounded and GST takes the residue, CGST
    /// takes the rounded half and SGST the remainder, and the grand total rounds half away from zero
    /// to a whole rupee — and none of it was copied from a run.
    /// </para>
    /// <para>
    /// The offers are built once and the scenarios are baskets over them, because what varies
    /// between scenarios is the quantity, the place of supply and the coupon, not the catalogue.
    /// Every promotion is scoped to its own offers: the collection shares one database, and a
    /// campaign with an empty scope would apply to every other test's basket as well as this one's.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Thirty_pricing_and_tax_scenarios_reproduce_exactly()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var pricing = new PricingScenario(admin, Cancellation);
        var shopper = CreateClient();

        var taxonomy = await catalogue.TaxonomyAsync();

        var here = await pricing.SellerRegisteredInAsync(sellers, PricingScenario.TelanganaGstCode);
        var away = await pricing.SellerRegisteredInAsync(sellers, PricingScenario.MaharashtraGstCode);

        var telangana = await pricing.StateAsync(PricingScenario.TelanganaGstCode);
        var maharashtra = await pricing.StateAsync(PricingScenario.MaharashtraGstCode);

        // Six rates the table decides, and one the table says nothing about.
        var a = await RatedOfferAsync(catalogue, pricing, taxonomy, here, 5m, 0m, 1000.00m);
        var b = await RatedOfferAsync(catalogue, pricing, taxonomy, here, 12m, 0m, 499.50m);
        var c = await RatedOfferAsync(catalogue, pricing, taxonomy, here, 18m, 0m, 2999.00m);
        var d = await RatedOfferAsync(catalogue, pricing, taxonomy, here, 28m, 12m, 15_000.00m);
        var e = await RatedOfferAsync(catalogue, pricing, taxonomy, here, 0m, 0m, 250.00m, productRate: 5m);
        var f = await RatedOfferAsync(catalogue, pricing, taxonomy, away, 18m, 0m, 2999.00m);

        // No rate recorded at all, so the product's own eighteen per cent is what stands.
        var g = await pricing.OfferAsync(catalogue, taxonomy, here, PricingScenario.NewHsn(), 18m, 1180.00m);

        var tenPercentOffTheOrder = await pricing.PromotionAsync(
            appliesTo: "Order",
            value: 10m,
            code: PricingScenario.NewCouponCode(),
            scope: new { listingIds = new[] { a.ListingId, b.ListingId, c.ListingId, d.ListingId, f.ListingId } });

        var hundredOffTheOrder = await pricing.PromotionAsync(
            type: "Fixed",
            appliesTo: "Order",
            value: 100m,
            code: PricingScenario.NewCouponCode(),
            scope: new { listingIds = new[] { a.ListingId } });

        var fivePercentOffTheLine = await pricing.PromotionAsync(
            appliesTo: "Line",
            value: 5m,
            code: PricingScenario.NewCouponCode(),
            scope: new { listingIds = new[] { c.ListingId } });

        var tenPercent = await CodeOfAsync(admin, tenPercentOffTheOrder);
        var hundredOff = await CodeOfAsync(admin, hundredOffTheOrder);
        var fivePercent = await CodeOfAsync(admin, fivePercentOffTheLine);

        // label, basket, place of supply, coupon, expected lines, expected quote totals.
        GoldenScenario[] scenarios =
        [
            new("5% intra, one unit", [(a, 1)], telangana, null,
                [new(a, 1000.00m, 0m, 0m, 1000.00m, 952.38m, 23.81m, 23.81m, 0m, 0m)],
                new(1000.00m, 0m, 952.38m, 47.62m, 0m, 1000m)),
            new("12% intra, one unit", [(b, 1)], telangana, null,
                [new(b, 499.50m, 0m, 0m, 499.50m, 445.98m, 26.76m, 26.76m, 0m, 0m)],
                new(499.50m, 0m, 445.98m, 53.52m, 0.50m, 500m)),
            new("18% intra, one unit", [(c, 1)], telangana, null,
                [new(c, 2999.00m, 0m, 0m, 2999.00m, 2541.53m, 228.74m, 228.73m, 0m, 0m)],
                new(2999.00m, 0m, 2541.53m, 457.47m, 0m, 2999m)),
            new("28% plus 12% cess, intra, one unit", [(d, 1)], telangana, null,
                [new(d, 15_000.00m, 0m, 0m, 15_000.00m, 10_714.29m, 1500.00m, 1500.00m, 0m, 1285.71m)],
                new(15_000.00m, 0m, 10_714.29m, 4285.71m, 0m, 15_000m)),
            new("nil rated, intra, one unit", [(e, 1)], telangana, null,
                [new(e, 250.00m, 0m, 0m, 250.00m, 250.00m, 0m, 0m, 0m, 0m)],
                new(250.00m, 0m, 250.00m, 0m, 0m, 250m)),
            new("5% intra, three units", [(a, 3)], telangana, null,
                [new(a, 3000.00m, 0m, 0m, 3000.00m, 2857.14m, 71.43m, 71.43m, 0m, 0m)],
                new(3000.00m, 0m, 2857.14m, 142.86m, 0m, 3000m)),
            new("12% intra, three units", [(b, 3)], telangana, null,
                [new(b, 1498.50m, 0m, 0m, 1498.50m, 1337.95m, 80.28m, 80.27m, 0m, 0m)],
                new(1498.50m, 0m, 1337.95m, 160.55m, 0.50m, 1499m)),
            new("18% intra, three units", [(c, 3)], telangana, null,
                [new(c, 8997.00m, 0m, 0m, 8997.00m, 7624.58m, 686.21m, 686.21m, 0m, 0m)],
                new(8997.00m, 0m, 7624.58m, 1372.42m, 0m, 8997m)),
            new("cess-bearing, intra, three units", [(d, 3)], telangana, null,
                [new(d, 45_000.00m, 0m, 0m, 45_000.00m, 32_142.86m, 4500.00m, 4500.00m, 0m, 3857.14m)],
                new(45_000.00m, 0m, 32_142.86m, 12_857.14m, 0m, 45_000m)),
            new("nil rated, intra, three units", [(e, 3)], telangana, null,
                [new(e, 750.00m, 0m, 0m, 750.00m, 750.00m, 0m, 0m, 0m, 0m)],
                new(750.00m, 0m, 750.00m, 0m, 0m, 750m)),
            new("18% inter, one unit", [(f, 1)], telangana, null,
                [new(f, 2999.00m, 0m, 0m, 2999.00m, 2541.53m, 0m, 0m, 457.47m, 0m)],
                new(2999.00m, 0m, 2541.53m, 457.47m, 0m, 2999m)),
            new("18% inter, two units", [(f, 2)], telangana, null,
                [new(f, 5998.00m, 0m, 0m, 5998.00m, 5083.05m, 0m, 0m, 914.95m, 0m)],
                new(5998.00m, 0m, 5083.05m, 914.95m, 0m, 5998m)),
            new("18% inter, three units", [(f, 3)], telangana, null,
                [new(f, 8997.00m, 0m, 0m, 8997.00m, 7624.58m, 0m, 0m, 1372.42m, 0m)],
                new(8997.00m, 0m, 7624.58m, 1372.42m, 0m, 8997m)),
            new("the same seller supplying into their own state", [(f, 1)], maharashtra, null,
                [new(f, 2999.00m, 0m, 0m, 2999.00m, 2541.53m, 228.74m, 228.73m, 0m, 0m)],
                new(2999.00m, 0m, 2541.53m, 457.47m, 0m, 2999m)),
            new("the local seller supplying out of state", [(a, 1)], maharashtra, null,
                [new(a, 1000.00m, 0m, 0m, 1000.00m, 952.38m, 0m, 0m, 47.62m, 0m)],
                new(1000.00m, 0m, 952.38m, 47.62m, 0m, 1000m)),
            new("cess-bearing, inter, one unit", [(d, 1)], maharashtra, null,
                [new(d, 15_000.00m, 0m, 0m, 15_000.00m, 10_714.29m, 0m, 0m, 3000.00m, 1285.71m)],
                new(15_000.00m, 0m, 10_714.29m, 4285.71m, 0m, 15_000m)),
            new("cess-bearing, inter, three units", [(d, 3)], maharashtra, null,
                [new(d, 45_000.00m, 0m, 0m, 45_000.00m, 32_142.86m, 0m, 0m, 9000.00m, 3857.14m)],
                new(45_000.00m, 0m, 32_142.86m, 12_857.14m, 0m, 45_000m)),
            new("an HSN the rate table says nothing about", [(g, 1)], telangana, null,
                [new(g, 1180.00m, 0m, 0m, 1180.00m, 1000.00m, 90.00m, 90.00m, 0m, 0m)],
                new(1180.00m, 0m, 1000.00m, 180.00m, 0m, 1180m)),
            new("10% off the order, taxed on what is left", [(c, 1)], telangana, tenPercent,
                [new(c, 2999.00m, 0m, 299.90m, 2699.10m, 2287.37m, 205.87m, 205.86m, 0m, 0m)],
                new(2999.00m, 299.90m, 2287.37m, 411.73m, -0.10m, 2699m)),
            new("a flat hundred off the order", [(a, 1)], telangana, hundredOff,
                [new(a, 1000.00m, 0m, 100.00m, 900.00m, 857.14m, 21.43m, 21.43m, 0m, 0m)],
                new(1000.00m, 100.00m, 857.14m, 42.86m, 0m, 900m)),
            new("5% off the line, taxed on what is left", [(c, 1)], telangana, fivePercent,
                [new(c, 2999.00m, 149.95m, 0m, 2849.05m, 2414.45m, 217.30m, 217.30m, 0m, 0m)],
                new(2999.00m, 149.95m, 2414.45m, 434.60m, -0.05m, 2849m)),
            new("a discount on a cess-bearing line reduces the cess too", [(d, 1)], telangana, tenPercent,
                [new(d, 15_000.00m, 0m, 1500.00m, 13_500.00m, 9642.86m, 1350.00m, 1350.00m, 0m, 1157.14m)],
                new(15_000.00m, 1500.00m, 9642.86m, 3857.14m, 0m, 13_500m)),
            new("a multi-vendor basket in two states", [(a, 1), (f, 1)], telangana, null,
                [
                    new(a, 1000.00m, 0m, 0m, 1000.00m, 952.38m, 23.81m, 23.81m, 0m, 0m),
                    new(f, 2999.00m, 0m, 0m, 2999.00m, 2541.53m, 0m, 0m, 457.47m, 0m),
                ],
                new(3999.00m, 0m, 3493.91m, 505.09m, 0m, 3999m)),
            new("a multi-vendor basket with an order coupon allocated across it", [(a, 1), (f, 1)], telangana, tenPercent,
                [
                    new(a, 1000.00m, 0m, 100.00m, 900.00m, 857.14m, 21.43m, 21.43m, 0m, 0m),
                    new(f, 2999.00m, 0m, 299.90m, 2699.10m, 2287.37m, 0m, 0m, 411.73m, 0m),
                ],
                new(3999.00m, 399.90m, 3144.51m, 454.59m, -0.10m, 3599m)),
            new("five rates in one basket", [(a, 1), (b, 1), (c, 1), (d, 1), (e, 1)], telangana, null,
                [
                    new(a, 1000.00m, 0m, 0m, 1000.00m, 952.38m, 23.81m, 23.81m, 0m, 0m),
                    new(b, 499.50m, 0m, 0m, 499.50m, 445.98m, 26.76m, 26.76m, 0m, 0m),
                    new(c, 2999.00m, 0m, 0m, 2999.00m, 2541.53m, 228.74m, 228.73m, 0m, 0m),
                    new(d, 15_000.00m, 0m, 0m, 15_000.00m, 10_714.29m, 1500.00m, 1500.00m, 0m, 1285.71m),
                    new(e, 250.00m, 0m, 0m, 250.00m, 250.00m, 0m, 0m, 0m, 0m),
                ],
                new(19_748.50m, 0m, 14_904.18m, 4844.32m, 0.50m, 19_749m)),
            new("an order coupon whose allocation does not divide evenly", [(a, 1), (b, 1)], telangana, tenPercent,
                [
                    new(a, 1000.00m, 0m, 100.00m, 900.00m, 857.14m, 21.43m, 21.43m, 0m, 0m),
                    new(b, 499.50m, 0m, 49.95m, 449.55m, 401.38m, 24.09m, 24.08m, 0m, 0m),
                ],
                new(1499.50m, 149.95m, 1258.52m, 91.03m, 0.45m, 1350m)),
            new("half a rupee rounds up, not to even", [(b, 1)], telangana, null,
                [new(b, 499.50m, 0m, 0m, 499.50m, 445.98m, 26.76m, 26.76m, 0m, 0m)],
                new(499.50m, 0m, 445.98m, 53.52m, 0.50m, 500m)),
            new("a nil-rated line is taxable value and nothing else", [(e, 2)], telangana, null,
                [new(e, 500.00m, 0m, 0m, 500.00m, 500.00m, 0m, 0m, 0m, 0m)],
                new(500.00m, 0m, 500.00m, 0m, 0m, 500m)),
            new("a coupon that names an offer not in the basket takes nothing", [(g, 1)], telangana, hundredOff,
                [new(g, 1180.00m, 0m, 0m, 1180.00m, 1000.00m, 90.00m, 90.00m, 0m, 0m)],
                new(1180.00m, 0m, 1000.00m, 180.00m, 0m, 1180m)),
            new("two units of the cess-bearing line, inter-state", [(d, 2)], maharashtra, null,
                [new(d, 30_000.00m, 0m, 0m, 30_000.00m, 21_428.57m, 0m, 0m, 6000.00m, 2571.43m)],
                new(30_000.00m, 0m, 21_428.57m, 8571.43m, 0m, 30_000m)),
        ];

        Assert.Equal(30, scenarios.Length);

        var failures = new List<string>();

        foreach (var scenario in scenarios)
        {
            var quote = await pricing.QuoteAsync(
                shopper,
                [.. scenario.Basket.Select(entry => (entry.Offer.ListingId, entry.Quantity))],
                scenario.PlaceOfSupply,
                scenario.CouponCode);

            Check(failures, scenario, quote);
            AssertReconciles(quote);
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    /// <summary>One expected line of a golden scenario.</summary>
    /// <param name="Offer">The offer it prices.</param>
    /// <param name="Gross">Unit price multiplied by quantity.</param>
    /// <param name="LineDiscount">Discount from a line-scoped promotion.</param>
    /// <param name="OrderDiscount">This line's share of an order-scoped promotion.</param>
    /// <param name="LineTotal">The gross less both discounts.</param>
    /// <param name="TaxableValue">The line total with the tax taken back out of it.</param>
    /// <param name="Cgst">Central GST.</param>
    /// <param name="Sgst">State GST.</param>
    /// <param name="Igst">Integrated GST.</param>
    /// <param name="Cess">Compensation cess.</param>
    private sealed record GoldenLine(
        PricedOffer Offer,
        decimal Gross,
        decimal LineDiscount,
        decimal OrderDiscount,
        decimal LineTotal,
        decimal TaxableValue,
        decimal Cgst,
        decimal Sgst,
        decimal Igst,
        decimal Cess);

    /// <summary>The figures a whole golden quote must land on.</summary>
    /// <param name="Subtotal">The lines' gross, before any discount.</param>
    /// <param name="DiscountTotal">Every discount together.</param>
    /// <param name="TaxableValue">What the tax was computed on.</param>
    /// <param name="TaxTotal">Every tax figure together.</param>
    /// <param name="RoundingAdjustment">What landing on a whole rupee cost.</param>
    /// <param name="GrandTotal">What the shopper pays.</param>
    private sealed record GoldenTotals(
        decimal Subtotal,
        decimal DiscountTotal,
        decimal TaxableValue,
        decimal TaxTotal,
        decimal RoundingAdjustment,
        decimal GrandTotal);

    /// <summary>One row of the golden table.</summary>
    /// <param name="Name">What it is, for the failure message.</param>
    /// <param name="Basket">The offers and their quantities.</param>
    /// <param name="PlaceOfSupply">The state the goods are going to.</param>
    /// <param name="CouponCode">A code the shopper typed, or null.</param>
    /// <param name="Lines">What each line must come to.</param>
    /// <param name="Totals">What the quote must come to.</param>
    private sealed record GoldenScenario(
        string Name,
        (PricedOffer Offer, int Quantity)[] Basket,
        Guid PlaceOfSupply,
        string? CouponCode,
        GoldenLine[] Lines,
        GoldenTotals Totals);

    /// <summary>
    /// Collects every disagreement rather than stopping at the first.
    /// </summary>
    /// <remarks>
    /// Deliberately not an assertion per figure. A rounding change breaks a great many of these at
    /// once, and a run that reports only the first of them costs an afternoon of re-running to find
    /// out whether the rest agree.
    /// </remarks>
    /// <param name="failures">Where to record what disagreed.</param>
    /// <param name="scenario">The scenario being checked.</param>
    /// <param name="quote">What the engine answered.</param>
    private static void Check(List<string> failures, GoldenScenario scenario, JsonElement quote)
    {
        void Expect(string what, decimal expected, decimal actual)
        {
            if (expected != actual)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{scenario.Name}: {what} should be {expected} but was {actual}."));
            }
        }

        foreach (var line in scenario.Lines)
        {
            var actual = PricingScenario.LineFor(quote, line.Offer.ListingId);
            var label = $"line {line.Offer.HsnCode}";

            Expect($"{label} gross", line.Gross, PricingScenario.Amount(actual, "gross"));
            Expect($"{label} line discount", line.LineDiscount, PricingScenario.Amount(actual, "lineDiscount"));
            Expect($"{label} order discount", line.OrderDiscount, PricingScenario.Amount(actual, "orderDiscountAllocated"));
            Expect($"{label} line total", line.LineTotal, PricingScenario.Amount(actual, "lineTotal"));
            Expect($"{label} taxable value", line.TaxableValue, PricingScenario.Amount(actual, "taxableValue"));
            Expect($"{label} CGST", line.Cgst, PricingScenario.Amount(actual, "cgst"));
            Expect($"{label} SGST", line.Sgst, PricingScenario.Amount(actual, "sgst"));
            Expect($"{label} IGST", line.Igst, PricingScenario.Amount(actual, "igst"));
            Expect($"{label} cess", line.Cess, PricingScenario.Amount(actual, "cess"));
        }

        if (quote.GetProperty("lines").GetArrayLength() != scenario.Lines.Length)
        {
            failures.Add($"{scenario.Name}: the quote had a different number of lines than the basket.");
        }

        Expect("subtotal", scenario.Totals.Subtotal, PricingScenario.Amount(quote, "subtotal"));
        Expect("discount total", scenario.Totals.DiscountTotal, PricingScenario.Amount(quote, "discountTotal"));
        Expect("taxable value", scenario.Totals.TaxableValue, PricingScenario.Amount(quote, "taxableValue"));
        Expect("tax total", scenario.Totals.TaxTotal, PricingScenario.Amount(quote, "taxTotal"));
        Expect("rounding adjustment", scenario.Totals.RoundingAdjustment, PricingScenario.Amount(quote, "roundingAdjustment"));
        Expect("grand total", scenario.Totals.GrandTotal, PricingScenario.Amount(quote, "grandTotal"));
    }

    /// <summary>
    /// The invariants every quote has to satisfy, whatever is in it.
    /// </summary>
    /// <remarks>
    /// These are the statements a GST auditor makes: the parts add up to the total, the tax figures
    /// add up to the tax, and the sellers' groups add up to the basket. They hold for every scenario
    /// above and for every other quote in this file, which is why they are asserted separately from
    /// the golden figures rather than being folded into them.
    /// </remarks>
    /// <param name="quote">The quote to check.</param>
    internal static void AssertReconciles(JsonElement quote)
    {
        var subtotal = PricingScenario.Amount(quote, "subtotal");
        var discount = PricingScenario.Amount(quote, "discountTotal");
        var shipping = PricingScenario.Amount(quote, "shipping");
        var codFee = PricingScenario.Amount(quote, "codFee");
        var rounding = PricingScenario.Amount(quote, "roundingAdjustment");
        var grandTotal = PricingScenario.Amount(quote, "grandTotal");
        var taxable = PricingScenario.Amount(quote, "taxableValue");
        var tax = PricingScenario.Amount(quote, "taxTotal");
        var wallet = PricingScenario.Amount(quote, "walletApplied");

        var lines = quote.GetProperty("lines").EnumerateArray().ToList();
        var net = lines.Sum(line => PricingScenario.Amount(line, "lineTotal"));

        Assert.Equal(
            PricingScenario.Amount(quote, "lineDiscountTotal") + PricingScenario.Amount(quote, "orderDiscountTotal"),
            discount);

        Assert.Equal(subtotal - discount, net);
        Assert.Equal(grandTotal, net + shipping + codFee + rounding);
        Assert.Equal(net + shipping + codFee, taxable + tax);
        Assert.Equal(grandTotal - wallet, PricingScenario.Amount(quote, "amountPayable"));

        Assert.Equal(
            tax,
            PricingScenario.Amount(quote, "cgstTotal")
            + PricingScenario.Amount(quote, "sgstTotal")
            + PricingScenario.Amount(quote, "igstTotal")
            + PricingScenario.Amount(quote, "cessTotal"));

        var groups = quote.GetProperty("vendorGroups").EnumerateArray().ToList();

        Assert.Equal(subtotal, groups.Sum(group => PricingScenario.Amount(group, "subtotal")));
        Assert.Equal(discount, groups.Sum(group => PricingScenario.Amount(group, "discount")));
        Assert.Equal(net + shipping, groups.Sum(group => PricingScenario.Amount(group, "total")));
        Assert.Equal(shipping, groups.Sum(group => PricingScenario.Amount(group, "shipping")));
        Assert.Equal(lines.Count, groups.Sum(group => group.GetProperty("lineIds").GetArrayLength()));
    }

    /// <summary>An offer whose rate the table decides, recorded against a code nothing else uses.</summary>
    /// <param name="catalogue">The taxonomy builder.</param>
    /// <param name="pricing">The pricing builder.</param>
    /// <param name="taxonomy">The vocabulary to file it under.</param>
    /// <param name="vendorId">The seller.</param>
    /// <param name="rate">The GST percentage the table records.</param>
    /// <param name="cessRate">The cess percentage the table records.</param>
    /// <param name="sellingPrice">What the offer asks.</param>
    /// <param name="productRate">The product's own rate, which the table's row must beat.</param>
    private static async Task<PricedOffer> RatedOfferAsync(
        CatalogScenario catalogue,
        PricingScenario pricing,
        CatalogTaxonomy taxonomy,
        Guid vendorId,
        decimal rate,
        decimal cessRate,
        decimal sellingPrice,
        decimal? productRate = null)
    {
        var hsn = PricingScenario.NewHsn();

        await pricing.TaxRateAsync(hsn, rate, cessRate);

        return await pricing.OfferAsync(catalogue, taxonomy, vendorId, hsn, productRate ?? rate, sellingPrice);
    }

    /// <summary>The code a promotion was created with, read back from the API that stored it.</summary>
    /// <param name="admin">A client signed in as platform staff.</param>
    /// <param name="promotionId">The promotion.</param>
    private static async Task<string> CodeOfAsync(HttpClient admin, Guid promotionId)
    {
        var promotion = await ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/promotions/{promotionId}", UriKind.Relative), Cancellation));

        return promotion.GetProperty("code").GetString()!;
    }

    /// <summary>Resolves a rate through the endpoint an operator would use to explain an invoice.</summary>
    /// <param name="admin">A client signed in as platform staff.</param>
    /// <param name="hsnCode">The code.</param>
    /// <param name="asOf">The date of supply, or null for today.</param>
    private static async Task<JsonElement> ResolveAsync(HttpClient admin, string hsnCode, DateOnly? asOf)
    {
        var query = asOf is { } date
            ? $"?hsnCode={hsnCode}&asOf={date:yyyy-MM-dd}"
            : $"?hsnCode={hsnCode}";

        return await ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/tax-rates/resolve{query}", UriKind.Relative), Cancellation));
    }
}
