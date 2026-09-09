using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The price-list walk and the quote engine against a live database: which list wins, what an offer
/// falls back to, the order the engine does things in, and the ceilings the anonymous quote is held
/// to.
/// </summary>
/// <remarks>
/// The engine's parts are unit-tested and pure. Everything here is about the half that is not: the
/// candidate query, the window filter applied in SQL, the vendor test applied in memory, the
/// settings the fees are read from, and the ordering — price, then promotions, then tax on what is
/// left, then shipping and the COD fee, then rounding, then store credit — which is the whole
/// design and which no unit test can observe.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class PricingQuoteTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// The walk picks the lowest priority number, then the highest quantity tier at or below the
    /// quantity, and ignores a list whose window is shut or which has been switched off.
    /// </summary>
    /// <remarks>
    /// Priority is not price and not recency: the cheaper list here deliberately loses, because a
    /// marketplace that resolved on "whichever is lowest" could not express a clearance that is
    /// meant to be beaten by a flash sale. The tier ladder is applied <em>within</em> the winning
    /// list for the same reason — a bulk price on a list that lost is a price on a list that lost.
    /// </remarks>
    [Fact]
    public async Task The_price_list_walk_picks_the_lowest_priority_the_right_tier_and_the_open_window()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var pricing = new PricingScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();
        var offer = await pricing.OfferAsync(catalogue, taxonomy, seller.Id, PricingScenario.NewHsn(), 18m, 1000.00m);

        var now = DateTimeOffset.UtcNow;

        // A shut window and one that has not opened. Both are cheaper than everything else and both
        // must be invisible, which is the only way to tell "in window" from "in the table".
        var closed = await pricing.PriceListAsync(1, startsAt: now.AddDays(-10), endsAt: now.AddDays(-1));
        var future = await pricing.PriceListAsync(1, startsAt: now.AddDays(10));

        var sale = await pricing.PriceListAsync(10, type: "Sale");
        var standing = await pricing.PriceListAsync(50, type: "Base");

        await ReadAsync(await pricing.SetPricesAsync(closed, [(offer.ListingId, 100.00m, 1)]));
        await ReadAsync(await pricing.SetPricesAsync(future, [(offer.ListingId, 50.00m, 1)]));
        await ReadAsync(await pricing.SetPricesAsync(sale, [(offer.ListingId, 900.00m, 1), (offer.ListingId, 750.00m, 5)]));
        await ReadAsync(await pricing.SetPricesAsync(standing, [(offer.ListingId, 800.00m, 1)]));

        // Lowest priority number wins, even though the list below it is cheaper.
        var one = await ResolveAsync(admin, offer.ListingId, 1);

        Assert.Equal(900.00m, PricingScenario.Amount(one, "unitPrice"));
        Assert.Equal(sale, one.GetProperty("priceListId").GetGuid());
        Assert.Equal(1, one.GetProperty("minQuantity").GetInt32());

        // Four units is still the base tier: the ladder takes the highest tier at or below the
        // quantity, not the next one up.
        Assert.Equal(900.00m, PricingScenario.Amount(await ResolveAsync(admin, offer.ListingId, 4), "unitPrice"));

        var five = await ResolveAsync(admin, offer.ListingId, 5);

        Assert.Equal(750.00m, PricingScenario.Amount(five, "unitPrice"));
        Assert.Equal(5, five.GetProperty("minQuantity").GetInt32());

        // And the quote agrees with the explanation, because it is the same walk.
        var bulk = await pricing.QuoteAsync(CreateClient(), [(offer.ListingId, 5)]);

        Assert.Equal(750.00m, PricingScenario.Amount(PricingScenario.LineFor(bulk, offer.ListingId), "unitPrice"));
        Assert.Equal(3750.00m, PricingScenario.Amount(bulk, "subtotal"));

        // Switching the winner off hands the offer to the next list that applies, rather than to
        // the offer's own price — the fallback is the end of the walk, not the first step of it.
        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/price-lists/{sale}/deactivate",
            new { },
            Cancellation));

        var afterTheSale = await ResolveAsync(admin, offer.ListingId, 1);

        Assert.Equal(800.00m, PricingScenario.Amount(afterTheSale, "unitPrice"));
        Assert.Equal(standing, afterTheSale.GetProperty("priceListId").GetGuid());
    }

    /// <summary>
    /// An offer no list prices keeps its own selling price, and a seller's list may not price
    /// another seller's offer — refused at the write, and ignored by the walk even if the row is
    /// planted behind the API.
    /// </summary>
    /// <remarks>
    /// Both halves matter and they are different guarantees. The handler's refusal is the message
    /// an operator reads; the walk's vendor test is what holds when a row arrives some other way —
    /// a restored backup, a migration, a future import. A price-list item carries no seller of its
    /// own, so nothing but the walk can make that judgement at read time.
    /// </remarks>
    [Fact]
    public async Task An_offer_no_list_prices_keeps_its_own_price_and_a_sellers_list_never_prices_another_sellers_offer()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var pricing = new PricingScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var mine = await sellers.ActiveAsync();
        var theirs = await sellers.ActiveAsync();

        var offer = await pricing.OfferAsync(catalogue, taxonomy, mine.Id, PricingScenario.NewHsn(), 18m, 1234.00m);

        // Nothing prices it, so the offer's own asking price stands and the explanation says no
        // list decided it.
        var unpriced = await ResolveAsync(admin, offer.ListingId, 1);

        Assert.Equal(1234.00m, PricingScenario.Amount(unpriced, "unitPrice"));
        Assert.Equal(JsonValueKind.Null, unpriced.GetProperty("priceListId").ValueKind);

        var quote = await pricing.QuoteAsync(CreateClient(), [(offer.ListingId, 2)]);

        Assert.Equal(1234.00m, PricingScenario.Amount(PricingScenario.LineFor(quote, offer.ListingId), "unitPrice"));
        Assert.Equal(2468.00m, PricingScenario.Amount(quote, "subtotal"));

        // The other seller's own list, priced at almost nothing. The write is refused outright.
        var rival = await pricing.PriceListAsync(1, theirs.Id);

        await RefusedAsync(
            await pricing.SetPricesAsync(rival, [(offer.ListingId, 1.00m, 1)]),
            HttpStatusCode.UnprocessableEntity,
            "PRICING_SCOPE");

        // And with the row planted behind the API, the walk still refuses to let it apply.
        var planted = await Database.ExecuteAsync(
            "INSERT INTO pricing.price_list_items "
            + "(id, price_list_id, listing_id, min_quantity, tenant_id, created_at, price_amount, price_currency_code) "
            + "SELECT gen_random_uuid(), list.id, $2, 1, list.tenant_id, now(), $3, 'INR' "
            + "FROM pricing.price_lists list WHERE list.id = $1",
            Cancellation,
            rival,
            offer.ListingId,
            1.00m);

        Assert.Equal(1, planted);

        var afterThePlant = await ResolveAsync(admin, offer.ListingId, 1);

        Assert.Equal(1234.00m, PricingScenario.Amount(afterThePlant, "unitPrice"));
        Assert.Equal(JsonValueKind.Null, afterThePlant.GetProperty("priceListId").ValueKind);

        // A platform-wide list, on the other hand, prices everybody's offers — which is the case
        // the vendor test exists to permit rather than to refuse.
        var everyone = await pricing.PriceListAsync(20);
        await ReadAsync(await pricing.SetPricesAsync(everyone, [(offer.ListingId, 999.00m, 1)]));

        Assert.Equal(999.00m, PricingScenario.Amount(await ResolveAsync(admin, offer.ListingId, 1), "unitPrice"));
    }

    /// <summary>
    /// One quote composes the price list, the promotion, the tax on what is left, shipping, the COD
    /// fee, the rupee rounding and the store credit — in that order, with every figure reconciling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the design and each step is observable only because the one before it moved the
    /// number. The price list is applied before the promotion, so the discount is a percentage of
    /// the list price and not of the offer's own; the tax is computed after the discount, so the
    /// taxable value is of what is left; the COD fee and shipping are taxed at the delivery rate
    /// rather than the goods'; the total rounds to a whole rupee and reports what that cost; and the
    /// wallet is applied last, to the rounded figure, so the rounding line still reconciles.
    /// </para>
    /// <para>
    /// The <c>pricing</c> settings section is written and put back. The collection shares one
    /// database, and a COD fee left switched on would quote a handling charge in every later test.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_quote_composes_price_promotion_tax_shipping_cod_rounding_and_credit_in_that_order()
    {
        SkipWithoutDocker();

        Factory.Features[Modules.Pricing.Infrastructure.PricingFeatureFlags.StoreCredit] = true;

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var pricing = new PricingScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await pricing.SellerRegisteredInAsync(sellers, PricingScenario.TelanganaGstCode);

        var hsn = PricingScenario.NewHsn();
        await pricing.TaxRateAsync(hsn, 18m);

        // The offer asks 999; the price list says 555.55. If the discount below comes to 55.56 then
        // it was taken off the list price, and if it comes to 99.90 it was taken off the offer's.
        var offer = await pricing.OfferAsync(catalogue, taxonomy, seller, hsn, 18m, 999.00m);
        var list = await pricing.PriceListAsync(10);

        await ReadAsync(await pricing.SetPricesAsync(list, [(offer.ListingId, 555.55m, 1)]));

        var campaign = await pricing.PromotionAsync(
            appliesTo: "Order",
            value: 10m,
            code: PricingScenario.NewCouponCode(),
            scope: new { listingIds = new[] { offer.ListingId } });

        var coupon = await CodeOfAsync(admin, campaign);

        var shopper = CreateClient();
        var customerId = await PricingScenario.ShopperAsync(shopper, Cancellation);

        await RunOnceAsync<Contracts.Pricing.IStoreCredit>(async (credit, cancellation) =>
        {
            var balance = await credit.CreditAsync(
                customerId,
                200m,
                Contracts.Pricing.StoreCreditReasons.Refund,
                "return",
                Guid.NewGuid(),
                cancellationToken: cancellation);

            Assert.Equal(200m, balance.Balance);
        });

        var original = await SettingsSectionAsync(admin, "pricing");

        try
        {
            await ReadAsync(await admin.PutAsJsonAsync(
                "/api/v1/admin/settings/pricing",
                new
                {
                    codHandlingFee = 50m,
                    shippingTaxRate = 18m,
                    roundToNearestRupee = true,
                    loyaltyAccrualPercent = 0m,
                    loyaltyMaxAccrualPerOrder = 0m,
                    walletMaxRedeemPercent = 100m,
                    walletExpiryDays = 0,
                },
                Cancellation));

            var quote = await pricing.QuoteAsync(
                shopper,
                [(offer.ListingId, 1)],
                await pricing.StateAsync(PricingScenario.TelanganaGstCode),
                coupon,
                paymentMethod: "CashOnDelivery",
                shippingAmount: 150.00m,
                walletRedeemRequested: 500.00m);

            var line = PricingScenario.LineFor(quote, offer.ListingId);

            // Price first: the list won, so the unit price is the list's and not the offer's.
            Assert.Equal(555.55m, PricingScenario.Amount(line, "unitPrice"));
            Assert.Equal(555.55m, PricingScenario.Amount(line, "gross"));

            // Then the promotion, on that price: ten per cent of 555.55 is 55.555, half away from
            // zero.
            Assert.Equal(55.56m, PricingScenario.Amount(line, "orderDiscountAllocated"));
            Assert.Equal(499.99m, PricingScenario.Amount(line, "lineTotal"));

            // Then the tax, on what is left rather than on the gross.
            Assert.Equal(423.72m, PricingScenario.Amount(line, "taxableValue"));
            Assert.Equal(38.14m, PricingScenario.Amount(line, "cgst"));
            Assert.Equal(38.13m, PricingScenario.Amount(line, "sgst"));
            Assert.Equal(0m, PricingScenario.Amount(line, "igst"));

            // Then shipping and the COD fee, taxed at the delivery rate rather than the goods'.
            Assert.Equal(150.00m, PricingScenario.Amount(quote, "shipping"));
            Assert.Equal(22.88m, PricingScenario.Amount(quote, "shippingTax"));
            Assert.Equal(50.00m, PricingScenario.Amount(quote, "codFee"));

            // Then the rounding, reported rather than absorbed: 699.99 becomes 700.
            Assert.Equal(0.01m, PricingScenario.Amount(quote, "roundingAdjustment"));
            Assert.Equal(700m, PricingScenario.Amount(quote, "grandTotal"));

            // And the wallet last of all, clamped to the balance and taken off the rounded total —
            // which is what leaves the rounding line reconcilable.
            Assert.Equal(200m, PricingScenario.Amount(quote, "walletApplied"));
            Assert.Equal(500m, PricingScenario.Amount(quote, "amountPayable"));

            Assert.Equal(593.21m, PricingScenario.Amount(quote, "taxableValue"));
            Assert.Equal(106.78m, PricingScenario.Amount(quote, "taxTotal"));

            TaxTests.AssertReconciles(quote);

            // Quoting writes nothing: the promotion was not redeemed and the credit was not spent,
            // because a cart is rendered many times and bought once.
            Assert.Equal(
                0,
                await Database.CountAsync(
                    "SELECT COUNT(*) FROM pricing.promotion_redemptions WHERE promotion_id = $1",
                    Cancellation,
                    campaign));

            await RunOnceAsync<Contracts.Pricing.IStoreCredit>(async (credit, cancellation) =>
                Assert.Equal(200m, (await credit.GetBalanceAsync(customerId, cancellation)).Balance));

            // The same basket without a wallet request lands on the same rounded total, which is the
            // proof that credit is applied after rounding rather than before it.
            var withoutCredit = await pricing.QuoteAsync(
                shopper,
                [(offer.ListingId, 1)],
                await pricing.StateAsync(PricingScenario.TelanganaGstCode),
                coupon,
                paymentMethod: "CashOnDelivery",
                shippingAmount: 150.00m);

            Assert.Equal(700m, PricingScenario.Amount(withoutCredit, "grandTotal"));
            Assert.Equal(0.01m, PricingScenario.Amount(withoutCredit, "roundingAdjustment"));
            Assert.Equal(0m, PricingScenario.Amount(withoutCredit, "walletApplied"));
            Assert.Equal(700m, PricingScenario.Amount(withoutCredit, "amountPayable"));

            // Prepaid charges no handling fee, which is why it is the default the endpoint assumes.
            var prepaid = await pricing.QuoteAsync(
                shopper,
                [(offer.ListingId, 1)],
                await pricing.StateAsync(PricingScenario.TelanganaGstCode),
                coupon,
                shippingAmount: 150.00m);

            Assert.Equal(0m, PricingScenario.Amount(prepaid, "codFee"));
        }
        finally
        {
            await ReadAsync(await admin.PutAsJsonAsync(
                "/api/v1/admin/settings/pricing",
                original,
                Cancellation));
        }
    }

    /// <summary>
    /// A signed-in caller's quote uses the id on their token and never one named in the body, so no
    /// caller can evaluate or spend another shopper's store credit.
    /// </summary>
    /// <remarks>
    /// The storefront body has no customer field at all, which is the design — but "the field does
    /// not exist" and "a field that arrives is ignored" are different statements, and only the
    /// second one is a security property. Both halves are asserted here against a body that really
    /// does carry somebody else's id: their balance is not spent, and their exhausted per-customer
    /// limit is not inherited.
    /// </remarks>
    [Fact]
    public async Task A_signed_in_callers_quote_uses_their_own_id_and_never_one_named_in_the_body()
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

        var rich = CreateClient();
        var poor = CreateClient();

        var richId = await PricingScenario.ShopperAsync(rich, Cancellation);
        var poorId = await PricingScenario.ShopperAsync(poor, Cancellation);

        await RunOnceAsync<Contracts.Pricing.IStoreCredit>(async (credit, cancellation) =>
            await credit.CreditAsync(
                richId,
                750m,
                Contracts.Pricing.StoreCreditReasons.Refund,
                "return",
                Guid.NewGuid(),
                cancellationToken: cancellation));

        // A coupon one shopper may use once, already used by the wealthy one.
        var campaign = await pricing.PromotionAsync(
            appliesTo: "Order",
            value: 10m,
            code: PricingScenario.NewCouponCode(),
            usageLimitPerCustomer: 1,
            scope: new { listingIds = new[] { offer.ListingId } });

        var coupon = await CodeOfAsync(admin, campaign);

        await RunOnceAsync<Contracts.Pricing.IPromotionLedger>(async (ledger, cancellation) =>
            Assert.Empty(await ledger.RedeemAsync(
                Guid.CreateVersion7(),
                richId,
                [new Contracts.Pricing.PromotionRedemptionRequest(campaign, 100m)],
                cancellation)));

        // The body really does carry somebody else's id. The storefront request record has no such
        // member, so this is a field arriving on the wire that the server has to drop rather than a
        // field the API offers — which is the difference between "not exposed" and "not honoured".
        var impersonating = await ReadAsync(await ImpersonatingQuoteAsync(poor, offer.ListingId, richId, coupon));

        // Their neighbour's seven hundred and fifty rupees stayed where they were: not one paisa of
        // store credit was applied, so the whole of the discounted total is still payable.
        Assert.Equal(0m, PricingScenario.Amount(impersonating, "walletApplied"));
        Assert.Equal(900m, PricingScenario.Amount(impersonating, "grandTotal"));
        Assert.Equal(900m, PricingScenario.Amount(impersonating, "amountPayable"));

        // And the coupon was evaluated against the caller's own history, not the one they named:
        // the wealthy shopper has used it and the poor one has not, so it applies.
        Assert.Equal(100m, PricingScenario.Amount(impersonating, "discountTotal"));
        Assert.True(PricingScenario.PromotionIn(impersonating, campaign).GetProperty("applied").GetBoolean());

        var honest = await ReadAsync(await ImpersonatingQuoteAsync(rich, offer.ListingId, poorId, coupon));

        // The mirror image, which is what makes the first half a statement about the token rather
        // than about the two shoppers: the wealthy caller spends their own credit and is refused
        // their own exhausted coupon, however hopefully the body names somebody else.
        Assert.Equal(0m, PricingScenario.Amount(honest, "discountTotal"));
        Assert.False(PricingScenario.PromotionIn(honest, campaign).GetProperty("applied").GetBoolean());
        Assert.Equal(500m, PricingScenario.Amount(honest, "walletApplied"));

        // An anonymous caller has no id at all, so a per-customer offer asks them to sign in rather
        // than silently applying.
        var anonymous = await pricing.QuoteAsync(CreateClient(), [(offer.ListingId, 1)], couponCode: coupon);

        Assert.Equal(0m, PricingScenario.Amount(anonymous, "discountTotal"));
        Assert.Equal(0m, PricingScenario.Amount(anonymous, "walletApplied"));
    }

    /// <summary>
    /// The quote refuses a basket past its line ceiling, considers no more promotions than it is
    /// configured to, and serves a realistic basket inside the storefront read budget.
    /// </summary>
    /// <remarks>
    /// It is the most expensive anonymous read on the platform — it touches the catalogue, the price
    /// lists, the rate table and every live campaign — so the ceilings are what stand between it and
    /// being a denial-of-service primitive with a friendly name. Both are asserted against a
    /// <em>configured</em> value rather than the shipped one, because a test that only ever sees the
    /// default cannot tell a wired-up option from a hard-coded constant.
    /// </remarks>
    [Fact]
    public async Task The_quote_stays_within_its_line_and_promotion_ceilings_and_serves_a_realistic_basket()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var pricing = new PricingScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();

        var offers = new List<PricedOffer>();

        for (var index = 0; index < 10; index++)
        {
            offers.Add(await pricing.OfferAsync(
                catalogue,
                taxonomy,
                seller.Id,
                PricingScenario.NewHsn(),
                18m,
                499.00m + index));
        }

        var basket = offers.ConvertAll(offer => (offer.ListingId, Quantity: 2)).ToArray();

        // The shipped ceiling, on the shipped host: a hundred and one lines is one too many, and
        // the refusal happens before anything is read.
        var overTheDefault = Enumerable.Range(0, 101)
            .Select(_ => (Guid.CreateVersion7(), 1))
            .ToArray();

        await RefusedAsync(
            await pricing.QuoteResponseAsync(CreateClient(), overTheDefault),
            HttpStatusCode.UnprocessableEntity,
            "PRICING_TOO_MANY_ITEMS");

        await using var constrained = NewFactory();

        constrained.Overrides["Pricing:MaxQuoteLines"] = "2";
        constrained.Overrides["Pricing:MaxPromotionCandidates"] = "1";

        using (var client = constrained.CreateClient())
        {
            var narrow = new PricingScenario(admin, Cancellation);

            await RefusedAsync(
                await narrow.QuoteResponseAsync(
                    client,
                    [(offers[0].ListingId, 1), (offers[1].ListingId, 1), (offers[2].ListingId, 1)]),
                HttpStatusCode.UnprocessableEntity,
                "PRICING_TOO_MANY_ITEMS");

            // Two is inside the configured ceiling, so the same host prices it.
            var inside = await narrow.QuoteAsync(client, [(offers[0].ListingId, 1), (offers[1].ListingId, 1)]);

            Assert.Equal(2, inside.GetProperty("lines").GetArrayLength());

            // Two automatic rules, and a host configured to consider one. The candidate query is
            // ordered by priority, so the one it considers is the one a merchandiser would expect.
            var first = await pricing.PromotionAsync(
                appliesTo: "Order",
                value: 5m,
                priority: 0,
                scope: new { listingIds = new[] { offers[0].ListingId } });

            var second = await pricing.PromotionAsync(
                appliesTo: "Order",
                value: 5m,
                priority: 1,
                scope: new { listingIds = new[] { offers[0].ListingId } });

            try
            {
                var capped = await narrow.QuoteAsync(client, [(offers[0].ListingId, 1)]);

                Assert.Equal(1, capped.GetProperty("promotions").GetArrayLength());
                Assert.True(PricingScenario.WasConsidered(capped, first));
                Assert.False(PricingScenario.WasConsidered(capped, second));
            }
            finally
            {
                await ReadAsync(await admin.PostAsJsonAsync(
                    $"/api/v1/admin/promotions/{first}/deactivate",
                    new { },
                    Cancellation));

                await ReadAsync(await admin.PostAsJsonAsync(
                    $"/api/v1/admin/promotions/{second}/deactivate",
                    new { },
                    Cancellation));
            }
        }

        // The latency baseline, on the shipped host and a realistic basket: ten offers, twenty
        // units, the whole engine. The bound is deliberately loose — this is a shared CI container,
        // and the failure worth catching is a per-line query creeping in, which is an order of
        // magnitude and not a few milliseconds.
        var anonymous = CreateClient();

        await pricing.QuoteAsync(anonymous, basket);

        var timings = new List<double>();

        for (var run = 0; run < 10; run++)
        {
            var stopwatch = Stopwatch.StartNew();

            var quote = await pricing.QuoteAsync(anonymous, basket);

            stopwatch.Stop();
            timings.Add(stopwatch.Elapsed.TotalMilliseconds);

            Assert.Equal(10, quote.GetProperty("lines").GetArrayLength());
            TaxTests.AssertReconciles(quote);
        }

        timings.Sort();

        var median = timings[timings.Count / 2];

        Assert.True(
            median < 2000d,
            string.Create(
                CultureInfo.InvariantCulture,
                $"A ten-line quote took a median of {median:0}ms, which is past the storefront read budget."));
    }

    /// <summary>
    /// The quote is rate-limited as an anonymous storefront read, and says so with the headers a
    /// client can act on.
    /// </summary>
    /// <remarks>
    /// Its own host with its own allowance, and a small one. A fixed window is shared state, so a
    /// test that used the collection's host would exhaust the allowance for everything after it and
    /// would itself depend on what ran before — and the number under test is a configured one, so
    /// turning it down proves the wiring rather than merely re-stating the shipped default.
    /// </remarks>
    [Fact]
    public async Task The_quote_is_rate_limited_as_an_anonymous_storefront_read()
    {
        SkipWithoutDocker();

        const int Allowance = 4;

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var pricing = new PricingScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();
        var offer = await pricing.OfferAsync(catalogue, taxonomy, seller.Id, PricingScenario.NewHsn(), 18m, 750.00m);

        await using var limited = NewFactory();

        limited.Overrides["RateLimiting:Enabled"] = "true";
        limited.Overrides["RateLimiting:Global:PermitLimit"] = "1000";
        limited.Overrides["RateLimiting:StorefrontRead:PermitLimit"] =
            Allowance.ToString(CultureInfo.InvariantCulture);
        limited.Overrides["RateLimiting:StorefrontRead:WindowSeconds"] = "60";

        using var client = limited.CreateClient();

        for (var attempt = 1; attempt <= Allowance; attempt++)
        {
            using var served = await pricing.QuoteResponseAsync(client, [(offer.ListingId, 1)]);

            Assert.True(
                served.StatusCode == HttpStatusCode.OK,
                $"quote {attempt} is inside the allowance but answered {(int)served.StatusCode}");
        }

        using var refused = await pricing.QuoteResponseAsync(client, [(offer.ListingId, 1)]);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType!.MediaType);

        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(Cancellation);

        Assert.Equal("RATE_LIMITED", problem.GetProperty("code").GetString());
        Assert.True(refused.Headers.RetryAfter!.Delta!.Value > TimeSpan.Zero);
        Assert.Equal(
            Allowance.ToString(CultureInfo.InvariantCulture),
            Assert.Single(refused.Headers.GetValues("X-RateLimit-Limit")));
    }

    /// <summary>
    /// Quotes a basket with somebody else's customer id written into the request body.
    /// </summary>
    /// <remarks>
    /// Sent as an anonymous object rather than as raw text so the field is unmistakably on the wire:
    /// <c>StoreQuoteBody</c> declares no customer, and an unmapped member is dropped by the
    /// serialiser, which is exactly the behaviour under test.
    /// </remarks>
    /// <param name="client">The caller, whose token is the only id that should count.</param>
    /// <param name="listingId">The offer to price.</param>
    /// <param name="customerId">Somebody else's id, hopefully.</param>
    /// <param name="couponCode">A code to try.</param>
    private static Task<HttpResponseMessage> ImpersonatingQuoteAsync(
        HttpClient client,
        Guid listingId,
        Guid customerId,
        string couponCode)
        => client.PostAsJsonAsync(
            "/api/v1/store/quote",
            new
            {
                lines = new[] { new { listingId, quantity = 1 } },
                customerId,
                couponCode,
                shippingAmount = 0m,
                walletRedeemRequested = 500m,
            },
            Cancellation);

    /// <summary>Explains what an offer costs and which list decided it.</summary>
    /// <param name="admin">A client signed in as platform staff.</param>
    /// <param name="listingId">The offer.</param>
    /// <param name="quantity">How many units, for the quantity tier.</param>
    private static async Task<JsonElement> ResolveAsync(HttpClient admin, Guid listingId, int quantity)
        => await ReadAsync(
            await admin.GetAsync(
                new Uri(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"/api/v1/admin/prices/resolve?listingId={listingId}&quantity={quantity}"),
                    UriKind.Relative),
                Cancellation));

    /// <summary>The code a promotion was created with, read back from the API that stored it.</summary>
    /// <param name="admin">A client signed in as platform staff.</param>
    /// <param name="promotionId">The promotion.</param>
    private static async Task<string> CodeOfAsync(HttpClient admin, Guid promotionId)
    {
        var promotion = await ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/promotions/{promotionId}", UriKind.Relative), Cancellation));

        return promotion.GetProperty("code").GetString()!;
    }

    /// <summary>One store settings section's current value, so a test can put it back.</summary>
    /// <param name="admin">A client signed in as platform staff.</param>
    /// <param name="key">The section key.</param>
    internal static async Task<JsonElement> SettingsSectionAsync(HttpClient admin, string key)
    {
        var settings = await ReadAsync(
            await admin.GetAsync(new Uri("/api/v1/admin/settings", UriKind.Relative), Cancellation));

        return Assert.Single(
            settings.GetProperty("sections").EnumerateArray(),
            section => section.GetProperty("key").GetString() == key)
            .GetProperty("value");
    }
}
