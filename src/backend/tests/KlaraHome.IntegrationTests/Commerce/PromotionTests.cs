using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.Contracts.Pricing;
using KlaraHome.IntegrationTests.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The promotion half of the module against a live database: which campaigns a quote even
/// considers, how a per-customer limit is counted, and the ledger that turns an applied promotion
/// into a redemption exactly once.
/// </summary>
/// <remarks>
/// The evaluator is pure and unit-tested. Everything here is the part that is not: a SQL predicate
/// that decides what reaches it, a conditional <c>UPDATE</c> that decides who gets the last use, and
/// a transaction that has to carry the claim, the row and the event together or none of them.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class PromotionTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// The candidate query offers the evaluator only live, in-window promotions, and of the coded
    /// ones only the code the shopper actually typed.
    /// </summary>
    /// <remarks>
    /// This is a filter in SQL rather than in the evaluator, and it runs on every cart render, so it
    /// is the one place where "considered but rejected" and "never seen" are different facts. An
    /// expired code has to be invisible rather than reported, and so does a live code the shopper
    /// did not type — a quote that listed every campaign on the platform would be handing a shopper
    /// the coupon book.
    /// </remarks>
    [Fact]
    public async Task The_candidate_query_offers_only_live_promotions_and_only_the_code_the_shopper_typed()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var pricing = new PricingScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();
        var offer = await pricing.OfferAsync(catalogue, taxonomy, seller.Id, PricingScenario.NewHsn(), 18m, 1000.00m);

        var only = new { listingIds = new[] { offer.ListingId } };
        var now = DateTimeOffset.UtcNow;

        var live = await pricing.PromotionAsync(code: PricingScenario.NewCouponCode(), scope: only);
        var expired = await pricing.PromotionAsync(
            code: PricingScenario.NewCouponCode(),
            scope: only,
            startsAt: now.AddDays(-30),
            endsAt: now.AddDays(-1));
        var notYet = await pricing.PromotionAsync(
            code: PricingScenario.NewCouponCode(),
            scope: only,
            startsAt: now.AddDays(30));
        var switchedOff = await pricing.PromotionAsync(
            code: PricingScenario.NewCouponCode(),
            scope: only,
            activate: false);
        var somebodyElses = await pricing.PromotionAsync(code: PricingScenario.NewCouponCode(), scope: only);

        var shopper = CreateClient();

        // No code typed: not one of the five is even considered, because every one of them carries
        // a code and nobody asked for it.
        var plain = await pricing.QuoteAsync(shopper, [(offer.ListingId, 1)]);

        foreach (var promotion in new[] { live, expired, notYet, switchedOff, somebodyElses })
        {
            Assert.False(PricingScenario.WasConsidered(plain, promotion));
        }

        Assert.Equal(0m, PricingScenario.Amount(plain, "discountTotal"));
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("couponRejection").ValueKind);

        // The code that was typed, and nothing else — the other live campaign stays invisible.
        var withCode = await pricing.QuoteAsync(
            shopper,
            [(offer.ListingId, 1)],
            couponCode: await CodeOfAsync(admin, live));

        Assert.True(PricingScenario.WasConsidered(withCode, live));
        Assert.False(PricingScenario.WasConsidered(withCode, somebodyElses));
        Assert.True(PricingScenario.PromotionIn(withCode, live).GetProperty("applied").GetBoolean());
        Assert.Equal(100m, PricingScenario.Amount(withCode, "discountTotal"));

        // Expired, not yet open, switched off: each is invisible rather than rejected, and the
        // shopper is told the code is not valid rather than being shown a campaign they cannot use.
        foreach (var (promotion, what) in new[]
                 {
                     (expired, "an expired code"),
                     (notYet, "a code whose campaign has not opened"),
                     (switchedOff, "a code whose campaign is switched off"),
                 })
        {
            var quote = await pricing.QuoteAsync(
                shopper,
                [(offer.ListingId, 1)],
                couponCode: await CodeOfAsync(admin, promotion));

            Assert.False(PricingScenario.WasConsidered(quote, promotion), what);
            Assert.Equal(0m, PricingScenario.Amount(quote, "discountTotal"));
            Assert.Equal("That code is not valid.", quote.GetProperty("couponRejection").GetString());
        }

        // A code nobody ever created is the same answer, which is what stops the endpoint being an
        // oracle for which codes exist.
        var invented = await pricing.QuoteAsync(shopper, [(offer.ListingId, 1)], couponCode: "NOSUCHCODE");

        Assert.False(PricingScenario.WasConsidered(invented, live));
        Assert.Equal(0m, PricingScenario.Amount(invented, "discountTotal"));
        Assert.Equal("That code is not valid.", invented.GetProperty("couponRejection").GetString());
    }

    /// <summary>
    /// A per-customer limit is counted from the redemption rows, and a redemption that was reversed
    /// still counts against the shopper even though the global counter gave the use back.
    /// </summary>
    /// <remarks>
    /// The asymmetry is deliberate and is the whole reason a reversal marks the row instead of
    /// deleting it. The global counter is a stock of uses and a cancelled order did not consume one,
    /// so it goes back; the per-customer limit is a statement about a person, and forgetting a
    /// cancelled order would let one shopper cycle a single-use coupon for ever by placing and
    /// cancelling.
    /// </remarks>
    [Fact]
    public async Task A_per_customer_limit_counts_redemptions_including_the_ones_that_were_reversed()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var pricing = new PricingScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();
        var offer = await pricing.OfferAsync(catalogue, taxonomy, seller.Id, PricingScenario.NewHsn(), 18m, 1000.00m);

        var campaign = await pricing.PromotionAsync(
            code: PricingScenario.NewCouponCode(),
            usageLimitPerCustomer: 1,
            scope: new { listingIds = new[] { offer.ListingId } });

        var coupon = await CodeOfAsync(admin, campaign);

        var first = CreateClient();
        var second = CreateClient();

        var firstId = await PricingScenario.ShopperAsync(first, Cancellation);
        _ = await PricingScenario.ShopperAsync(second, Cancellation);
        var order = Guid.CreateVersion7();

        // Never used it, so it applies.
        Assert.Equal(
            100m,
            PricingScenario.Amount(
                await pricing.QuoteAsync(first, [(offer.ListingId, 1)], couponCode: coupon),
                "discountTotal"));

        await RunOnceAsync<IPromotionLedger>(async (ledger, cancellation) =>
            Assert.Empty(await ledger.RedeemAsync(
                order,
                firstId,
                [new PromotionRedemptionRequest(campaign, 100m)],
                cancellation)));

        var used = await pricing.QuoteAsync(first, [(offer.ListingId, 1)], couponCode: coupon);

        Assert.Equal(0m, PricingScenario.Amount(used, "discountTotal"));
        Assert.Equal("You have already used this offer.", used.GetProperty("couponRejection").GetString());

        // Their order is cancelled. The use goes back to the pool…
        await RunOnceAsync<IPromotionLedger>(async (ledger, cancellation) =>
            Assert.Equal(1, await ledger.ReverseAsync(order, cancellation)));

        Assert.Equal(
            0,
            await Database.ScalarAsync<int>(
                "SELECT usage_count FROM pricing.promotions WHERE id = $1",
                Cancellation,
                campaign));

        // …and the row is still there, marked rather than deleted, which is what the limit counts.
        var rows = await Database.RowsAsync(
            "SELECT status, reversed_at FROM pricing.promotion_redemptions WHERE promotion_id = $1",
            Cancellation,
            campaign);

        Assert.Equal("Reversed", Assert.Single(rows)["status"]);
        Assert.NotNull(rows[0]["reversed_at"]);

        // So the same shopper still cannot have it a second time.
        var afterCancelling = await pricing.QuoteAsync(first, [(offer.ListingId, 1)], couponCode: coupon);

        Assert.Equal(0m, PricingScenario.Amount(afterCancelling, "discountTotal"));
        Assert.Equal(
            "You have already used this offer.",
            afterCancelling.GetProperty("couponRejection").GetString());

        // A different shopper is untouched by any of it: the limit is per customer, and the use
        // that went back to the pool is theirs to take.
        Assert.Equal(
            100m,
            PricingScenario.Amount(
                await pricing.QuoteAsync(second, [(offer.ListingId, 1)], couponCode: coupon),
                "discountTotal"));
    }

    /// <summary>
    /// The same order redeeming twice redeems once: one row, one increment, one event.
    /// </summary>
    /// <remarks>
    /// The events that drive placement are delivered at least once, so this is the ordinary case
    /// rather than an edge one. The second call has to be a no-op and not a refusal — a refusal
    /// would be recorded on the order as a promotion that could not be honoured, for a promotion
    /// that already had been.
    /// </remarks>
    [Fact]
    public async Task An_order_redeeming_twice_redeems_once_and_increments_the_counter_once()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var pricing = new PricingScenario(admin, Cancellation);

        var campaign = await pricing.PromotionAsync(
            code: PricingScenario.NewCouponCode(),
            usageLimitTotal: 5,
            scope: new { listingIds = new[] { Guid.CreateVersion7() } });

        var order = Guid.CreateVersion7();
        var customer = Guid.CreateVersion7();

        for (var delivery = 1; delivery <= 3; delivery++)
        {
            await RunOnceAsync<IPromotionLedger>(async (ledger, cancellation) =>
                Assert.Empty(await ledger.RedeemAsync(
                    order,
                    customer,
                    [new PromotionRedemptionRequest(campaign, 100m)],
                    cancellation)));
        }

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM pricing.promotion_redemptions WHERE promotion_id = $1 AND order_id = $2",
                Cancellation,
                campaign,
                order));

        Assert.Equal(
            1,
            await Database.ScalarAsync<int>(
                "SELECT usage_count FROM pricing.promotions WHERE id = $1",
                Cancellation,
                campaign));

        // And exactly one announcement, because a consumer counting campaign performance from the
        // event would otherwise count this order three times.
        Assert.Equal(1, await EventsAboutAsync(order, nameof(PromotionRedeemed)));
    }

    /// <summary>
    /// Two orders placed at the same instant against a promotion with one use left produce one
    /// redemption and one refusal.
    /// </summary>
    /// <remarks>
    /// Read the count, decide, write it back — and both orders see the last use available. The
    /// conditional <c>UPDATE</c> is what makes a single-use coupon single-use: the database decides,
    /// and the loser is told, because the total the shopper was quoted is no longer the total.
    /// </remarks>
    [Fact]
    public async Task Two_orders_racing_for_the_last_use_produce_one_redemption_and_one_refusal()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var pricing = new PricingScenario(admin, Cancellation);

        var campaign = await pricing.PromotionAsync(
            code: PricingScenario.NewCouponCode(),
            usageLimitTotal: 1,
            scope: new { listingIds = new[] { Guid.CreateVersion7() } });

        var request = new PromotionRedemptionRequest(campaign, 100m);

        var outcomes = await Task.WhenAll(
            InScopeAsync<IPromotionLedger, IReadOnlyList<Guid>>(
                (ledger, cancellation) => ledger.RedeemAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), [request], cancellation).AsTask()),
            InScopeAsync<IPromotionLedger, IReadOnlyList<Guid>>(
                (ledger, cancellation) => ledger.RedeemAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), [request], cancellation).AsTask()));

        Assert.Equal(1, outcomes.Count(refused => refused.Count == 0));
        Assert.Equal(1, outcomes.Count(refused => refused.Count == 1 && refused[0] == campaign));

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM pricing.promotion_redemptions WHERE promotion_id = $1",
                Cancellation,
                campaign));

        Assert.Equal(
            1,
            await Database.ScalarAsync<int>(
                "SELECT usage_count FROM pricing.promotions WHERE id = $1",
                Cancellation,
                campaign));
    }

    /// <summary>
    /// The claim, the redemption row and the outbox event commit together: a conflict part-way
    /// leaves none of the three.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The claim is an <c>ExecuteUpdateAsync</c>, which sends its own statement to the database
    /// rather than going through the change tracker, so whether it is inside the explicit
    /// transaction is a real question and not a formality. If it were outside, a redemption whose
    /// row was refused would leave the counter one higher than the rows that are supposed to explain
    /// it, and the drift would be permanent and invisible.
    /// </para>
    /// <para>
    /// The conflict is arranged rather than raced for. A second connection inserts the redemption
    /// row for this <c>(promotion, order)</c> and holds its transaction open: the row is invisible
    /// to the "already redeemed" read, so the ledger claims a use and then blocks on the unique
    /// index. Committing the other connection turns that block into a refusal at exactly the point
    /// the assertion is about — deterministically, rather than one run in five.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_redemption_commits_the_claim_the_row_and_the_event_together()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var pricing = new PricingScenario(admin, Cancellation);

        var campaign = await pricing.PromotionAsync(
            code: PricingScenario.NewCouponCode(),
            usageLimitTotal: 10,
            scope: new { listingIds = new[] { Guid.CreateVersion7() } });

        var order = Guid.CreateVersion7();
        var customer = Guid.CreateVersion7();
        var request = new PromotionRedemptionRequest(campaign, 100m);

        var tenantId = await Database.ScalarAsync<Guid>(
            "SELECT tenant_id FROM pricing.promotions WHERE id = $1",
            Cancellation,
            campaign);

        await using var rival = new NpgsqlConnection(Database.ConnectionString);
        await rival.OpenAsync(Cancellation);

        var held = await rival.BeginTransactionAsync(Cancellation);
        Exception? failure;

        await using (held.ConfigureAwait(false))
        {
            await using (var insert = new NpgsqlCommand(
                "INSERT INTO pricing.promotion_redemptions "
                + "(id, promotion_id, code, customer_id, order_id, discount_amount, redeemed_at, status, "
                + " tenant_id, created_at) "
                + "VALUES (gen_random_uuid(), $1, NULL, NULL, $2, 100, now(), 'Redeemed', $3, now())",
                rival))
            {
                insert.Parameters.Add(new NpgsqlParameter { Value = campaign });
                insert.Parameters.Add(new NpgsqlParameter { Value = order });
                insert.Parameters.Add(new NpgsqlParameter { Value = tenantId });

                await insert.ExecuteNonQueryAsync(Cancellation);
            }

            // Uncommitted, so the ledger's "already redeemed" read cannot see it and will claim.
            var redeeming = AttemptAsync(order, customer, request);

            await WaitUntilBlockedAsync("%promotion_redemptions%");

            await held.CommitAsync(Cancellation);

            failure = await redeeming;
        }

        // The insert was refused, so the whole call was: no second row, and — the assertion this
        // test exists for — no claim left behind on the counter either.
        Assert.NotNull(failure);

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM pricing.promotion_redemptions WHERE promotion_id = $1 AND order_id = $2",
                Cancellation,
                campaign,
                order));

        Assert.Equal(0, await UsageCountAsync(campaign));

        // Nothing was announced either. An event published for a redemption that was rolled back
        // would have reporting counting a discount no order ever carried.
        Assert.Equal(0, await EventsAboutAsync(order, nameof(PromotionRedeemed)));

        // And with the conflict committed, the next delivery is the ordinary idempotent no-op: the
        // row is now visible to the "already redeemed" read, so nothing is claimed at all.
        Assert.Null(await AttemptAsync(order, customer, request));
        Assert.Equal(0, await UsageCountAsync(campaign));
    }

    /// <summary>
    /// Waits until a backend is blocked on a lock taken against a given table, rather than sleeping.
    /// </summary>
    /// <remarks>
    /// The redemption above has to have reached its insert before the other transaction commits, or
    /// the test proves nothing. A fixed delay would be either slow or flaky depending on the
    /// machine; the database can be asked directly, and naming the table keeps an unrelated
    /// background query from answering for it.
    /// </remarks>
    /// <param name="table">A fragment of the statement to wait for, as a SQL <c>LIKE</c> pattern.</param>
    private async Task WaitUntilBlockedAsync(string table)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var waiting = await Database.CountAsync(
                "SELECT COUNT(*) FROM pg_stat_activity "
                + "WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE $1",
                Cancellation,
                table);

            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), Cancellation);
        }

        Assert.Fail("The redemption never reached the row it was meant to conflict with.");
    }

    /// <summary>
    /// Reversing gives back exactly the uses it took, marks the rows rather than deleting them,
    /// floors the counter at zero, and reports nothing the second time.
    /// </summary>
    /// <remarks>
    /// A counter driven negative would make every later limit check wrong in the shopper's favour,
    /// and a reversal that reported its work twice would let a cancellation credit a campaign's
    /// budget back twice over. Both are money.
    /// </remarks>
    [Fact]
    public async Task Reversing_gives_back_exactly_the_uses_it_took_and_reports_nothing_the_second_time()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var pricing = new PricingScenario(admin, Cancellation);

        var campaign = await pricing.PromotionAsync(
            code: PricingScenario.NewCouponCode(),
            usageLimitTotal: 10,
            scope: new { listingIds = new[] { Guid.CreateVersion7() } });

        var orders = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };

        foreach (var order in orders)
        {
            await RunOnceAsync<IPromotionLedger>(async (ledger, cancellation) =>
                Assert.Empty(await ledger.RedeemAsync(
                    order,
                    Guid.CreateVersion7(),
                    [new PromotionRedemptionRequest(campaign, 100m)],
                    cancellation)));
        }

        Assert.Equal(3, await UsageCountAsync(campaign));

        await RunOnceAsync<IPromotionLedger>(async (ledger, cancellation) =>
            Assert.Equal(1, await ledger.ReverseAsync(orders[0], cancellation)));

        Assert.Equal(2, await UsageCountAsync(campaign));

        // A second cancellation of the same order gives nothing further back.
        await RunOnceAsync<IPromotionLedger>(async (ledger, cancellation) =>
            Assert.Equal(0, await ledger.ReverseAsync(orders[0], cancellation)));

        Assert.Equal(2, await UsageCountAsync(campaign));

        // Nor does an order that never used it at all.
        await RunOnceAsync<IPromotionLedger>(async (ledger, cancellation) =>
            Assert.Equal(0, await ledger.ReverseAsync(Guid.CreateVersion7(), cancellation)));

        // Three rows, one of them reversed and two still standing: marked, never deleted.
        Assert.Equal(
            3,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM pricing.promotion_redemptions WHERE promotion_id = $1",
                Cancellation,
                campaign));

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM pricing.promotion_redemptions "
                + "WHERE promotion_id = $1 AND status = 'Reversed' AND reversed_at IS NOT NULL",
                Cancellation,
                campaign));

        // A counter somebody has reset by hand must not be driven below zero by the reversals that
        // follow it, because a negative count would make every later limit check wrong.
        await Database.ExecuteAsync(
            "UPDATE pricing.promotions SET usage_count = 0 WHERE id = $1",
            Cancellation,
            campaign);

        await RunOnceAsync<IPromotionLedger>(async (ledger, cancellation) =>
            Assert.Equal(1, await ledger.ReverseAsync(orders[1], cancellation)));

        Assert.Equal(0, await UsageCountAsync(campaign));
    }

    /// <summary>
    /// Switching <c>pricing.coupons</c> off stops every code at once, and a code typed while it is
    /// off is reported as not being accepted rather than as invalid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The switch exists for an incident — a code being shared publicly — where an operator has to
    /// stop it now and cannot deactivate campaigns one at a time. What it must not do is tell the
    /// shopper their code is wrong: it works again tomorrow, and "invalid" sends them to support
    /// with a code that will be fine by the time anybody looks at it.
    /// </para>
    /// <para>
    /// Automatic cart rules are deliberately unaffected. The flag is about codes, and a store that
    /// lost its free-shipping rule because a coupon leaked would be paying twice for the incident.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Switching_coupons_off_stops_every_code_and_says_they_are_not_being_accepted()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var pricing = new PricingScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();
        var offer = await pricing.OfferAsync(catalogue, taxonomy, seller.Id, PricingScenario.NewHsn(), 18m, 1000.00m);

        var only = new { listingIds = new[] { offer.ListingId } };

        // The code goes first on priority and is exclusive, so with the switch on it takes a hundred
        // and the rule below it reports that it cannot be combined. With the switch off the code is
        // not a candidate at all and the rule takes fifty — which is what makes the two states tell
        // each other apart by the money rather than only by the wording.
        var coded = await pricing.PromotionAsync(
            code: PricingScenario.NewCouponCode(),
            value: 10m,
            priority: 5,
            scope: only);

        var automatic = await pricing.PromotionAsync(value: 5m, priority: 100, scope: only);

        try
        {
            var coupon = await CodeOfAsync(admin, coded);

            var on = await pricing.QuoteAsync(CreateClient(), [(offer.ListingId, 1)], couponCode: coupon);

            Assert.True(PricingScenario.WasConsidered(on, coded));
            Assert.True(PricingScenario.PromotionIn(on, coded).GetProperty("applied").GetBoolean());
            Assert.False(PricingScenario.PromotionIn(on, automatic).GetProperty("applied").GetBoolean());
            Assert.Equal(100m, PricingScenario.Amount(on, "discountTotal"));

            await using var stopped = NewFactory();

            stopped.Features[Modules.Pricing.Infrastructure.PricingFeatureFlags.Coupons] = false;

            using var client = stopped.CreateClient();

            var off = await pricing.QuoteAsync(client, [(offer.ListingId, 1)], couponCode: coupon);

            // The code is not even a candidate any more…
            Assert.False(PricingScenario.WasConsidered(off, coded));

            // …and the shopper is told the truth about why, which is not that it is invalid.
            Assert.Equal(
                "Coupon codes are not being accepted at the moment.",
                off.GetProperty("couponRejection").GetString());

            // The automatic rule is untouched: five per cent still comes off.
            Assert.True(PricingScenario.WasConsidered(off, automatic));
            Assert.Equal(50m, PricingScenario.Amount(off, "discountTotal"));
        }
        finally
        {
            await ReadAsync(await admin.PostAsJsonAsync(
                $"/api/v1/admin/promotions/{automatic}/deactivate",
                new { },
                Cancellation));
        }
    }

    /// <summary>Runs one redemption in its own scope and answers the failure, or null when it stood.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="customerId">The shopper.</param>
    /// <param name="request">What to redeem.</param>
    private async Task<Exception?> AttemptAsync(Guid orderId, Guid customerId, PromotionRedemptionRequest request)
    {
        try
        {
            await InScopeAsync<IPromotionLedger, IReadOnlyList<Guid>>(
                (ledger, cancellation) => ledger.RedeemAsync(orderId, customerId, [request], cancellation).AsTask());

            return null;
        }
        catch (DbUpdateException exception)
        {
            return exception;
        }
    }

    /// <summary>How many times a promotion's derived counter says it has been used.</summary>
    /// <param name="promotionId">The promotion.</param>
    private async Task<int> UsageCountAsync(Guid promotionId)
        => await Database.ScalarAsync<int>(
            "SELECT usage_count FROM pricing.promotions WHERE id = $1",
            Cancellation,
            promotionId);

    /// <summary>How many events of a kind the outbox holds about one order.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="eventType">The contract type's name.</param>
    private async Task<long> EventsAboutAsync(Guid orderId, string eventType)
        => await Database.CountAsync(
            "SELECT COUNT(*) FROM platform.outbox_messages WHERE type LIKE $1 AND payload::text LIKE $2",
            Cancellation,
            $"%{eventType}%",
            $"%{orderId}%");

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
