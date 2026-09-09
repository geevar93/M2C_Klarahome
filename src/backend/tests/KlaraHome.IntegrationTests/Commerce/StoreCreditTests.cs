using System.Net;
using System.Net.Http.Json;
using KlaraHome.Contracts.Pricing;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.IntegrationTests.Identity;
using KlaraHome.Modules.Pricing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The store-credit wallet against a live database: idempotency on the caller's reference, the
/// unique index that enforces it when a pre-check cannot, the all-or-nothing debit, and the switch
/// the whole surface sits behind.
/// </summary>
/// <remarks>
/// This is the one place in the platform where a bug hands out money. The events that credit a
/// wallet — a refund, a cancellation, a loyalty accrual — are delivered at least once, so "credits
/// once" is the ordinary requirement rather than a hardened one, and the balance is a derived cache
/// that has to stay equal to the rows it is derived from or a shopper is told they have something
/// they do not.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class StoreCreditTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// The same refund delivered twice credits once, the same order paid twice debits once, and the
    /// unique index is what holds when the pre-check cannot see the other write.
    /// </summary>
    /// <remarks>
    /// The service reads for an earlier movement with the same reference and returns it if it finds
    /// one, which handles redelivery. It cannot handle a write it has not yet been able to see, and
    /// that is what the partial unique index on <c>(wallet, reference type, reference, type)</c> is
    /// for. Here the conflicting row is inserted on a second connection and held uncommitted, so the
    /// pre-check genuinely misses it and the index genuinely decides — the balance and the ledger
    /// move together or not at all.
    /// </remarks>
    [Fact]
    public async Task A_refund_delivered_twice_credits_once_and_the_unique_index_is_what_enforces_it()
    {
        SkipWithoutDocker();

        Factory.Features[PricingFeatureFlags.StoreCredit] = true;

        var shopper = CreateClient();
        var customerId = await PricingScenario.ShopperAsync(shopper, Cancellation);

        var refund = Guid.CreateVersion7();
        var order = Guid.CreateVersion7();

        // The same refund event, three times.
        for (var delivery = 1; delivery <= 3; delivery++)
        {
            await RunOnceAsync<IStoreCredit>(async (credit, cancellation) =>
            {
                var balance = await credit.CreditAsync(
                    customerId,
                    500m,
                    StoreCreditReasons.Refund,
                    "return",
                    refund,
                    cancellationToken: cancellation);

                Assert.Equal(500m, balance.Balance);
                Assert.True(balance.IsActive);
            });
        }

        Assert.Equal(1, await MovementsAsync(customerId, "Credit"));
        Assert.Equal(500m, await BalanceAsync(customerId));

        // And the same order paying with credit, three times: the answer is what it took the first
        // time rather than a second debit, because the caller has to be able to retry.
        for (var delivery = 1; delivery <= 3; delivery++)
        {
            await RunOnceAsync<IStoreCredit>(async (credit, cancellation) =>
                Assert.Equal(
                    200m,
                    await credit.RedeemAsync(
                        customerId,
                        200m,
                        StoreCreditReasons.OrderPayment,
                        "order",
                        order,
                        cancellation)));
        }

        Assert.Equal(1, await MovementsAsync(customerId, "Debit"));
        Assert.Equal(300m, await BalanceAsync(customerId));

        // Now the case the pre-check cannot see: a movement for the same reference, written on
        // another connection and not yet committed.
        var walletId = await Database.ScalarAsync<Guid>(
            "SELECT id FROM pricing.wallets WHERE customer_id = $1",
            Cancellation,
            customerId);

        var tenantId = await Database.ScalarAsync<Guid>(
            "SELECT tenant_id FROM pricing.wallets WHERE customer_id = $1",
            Cancellation,
            customerId);

        var second = Guid.CreateVersion7();

        await using var rival = new NpgsqlConnection(Database.ConnectionString);
        await rival.OpenAsync(Cancellation);

        var held = await rival.BeginTransactionAsync(Cancellation);
        Exception? failure;

        await using (held.ConfigureAwait(false))
        {
            await using (var insert = new NpgsqlCommand(
                "INSERT INTO pricing.wallet_transactions "
                + "(id, wallet_id, type, reason, reference_type, reference_id, occurred_at, tenant_id, "
                + " created_at, amount_amount, amount_currency_code, balance_after_amount, "
                + " balance_after_currency_code) "
                + "VALUES (gen_random_uuid(), $1, 'Credit', 'refund', 'return', $2, now(), $3, now(), "
                + " 100, 'INR', 400, 'INR')",
                rival))
            {
                insert.Parameters.Add(new NpgsqlParameter { Value = walletId });
                insert.Parameters.Add(new NpgsqlParameter { Value = second });
                insert.Parameters.Add(new NpgsqlParameter { Value = tenantId });

                await insert.ExecuteNonQueryAsync(Cancellation);
            }

            var crediting = CreditAsync(customerId, 100m, second);

            await WaitUntilBlockedAsync("%wallet_transactions%");

            await held.CommitAsync(Cancellation);

            failure = await crediting;
        }

        Assert.NotNull(failure);

        // One movement for that reference, and the balance cache moved with it rather than
        // separately: the refused insert took the balance update down with it.
        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM pricing.wallet_transactions WHERE reference_id = $1",
                Cancellation,
                second));

        Assert.Equal(300m, await BalanceAsync(customerId));

        // A different reference is a different movement, which is the other half of "keyed on the
        // reference": idempotency must not become deduplication of unrelated refunds.
        await RunOnceAsync<IStoreCredit>(async (credit, cancellation) =>
            Assert.Equal(
                800m,
                (await credit.CreditAsync(
                    customerId,
                    500m,
                    StoreCreditReasons.Refund,
                    "return",
                    Guid.CreateVersion7(),
                    cancellationToken: cancellation)).Balance));

        // Three credits: the refund, the row the other connection committed, and this one.
        Assert.Equal(3, await MovementsAsync(customerId, "Credit"));
    }

    /// <summary>
    /// A debit larger than the balance takes nothing at all, and the balance cache always equals
    /// the sum of the movements behind it.
    /// </summary>
    /// <remarks>
    /// All or nothing is the point. A partial debit would leave the order underpaid by a figure
    /// nobody quoted and the caller with no way to tell the shopper what happened, so the wallet
    /// answers zero and lets the caller decide. The cache invariant is asserted from the ledger
    /// rather than from the API, because the two agreeing is exactly what is under test.
    /// </remarks>
    [Fact]
    public async Task A_wallet_debit_is_all_or_nothing_and_the_balance_always_equals_its_transactions()
    {
        SkipWithoutDocker();

        Factory.Features[PricingFeatureFlags.StoreCredit] = true;

        var admin = await SignedInAdministratorAsync();
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

        // More than there is: nothing moves, and the answer says so.
        await RunOnceAsync<IStoreCredit>(async (credit, cancellation) =>
            Assert.Equal(
                0m,
                await credit.RedeemAsync(
                    customerId,
                    250m,
                    StoreCreditReasons.OrderPayment,
                    "order",
                    Guid.CreateVersion7(),
                    cancellation)));

        Assert.Equal(0, await MovementsAsync(customerId, "Debit"));
        Assert.Equal(100m, await BalanceAsync(customerId));

        // Exactly what there is: taken whole.
        await RunOnceAsync<IStoreCredit>(async (credit, cancellation) =>
            Assert.Equal(
                100m,
                await credit.RedeemAsync(
                    customerId,
                    100m,
                    StoreCreditReasons.OrderPayment,
                    "order",
                    Guid.CreateVersion7(),
                    cancellation)));

        Assert.Equal(0m, await BalanceAsync(customerId));

        // A staff adjustment is the same rule through a different door, and it is refused with a
        // code an operator can act on rather than a constraint violation.
        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/wallets/{customerId}/adjust",
            new { amount = 250m, reason = StoreCreditReasons.Adjustment, note = "Goodwill.", expiresAt = (DateTimeOffset?)null },
            Cancellation));

        await RefusedAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/wallets/{customerId}/adjust",
                new { amount = -400m, reason = StoreCreditReasons.Adjustment, note = "Too much.", expiresAt = (DateTimeOffset?)null },
                Cancellation),
            HttpStatusCode.Conflict,
            "PRICING_INSUFFICIENT_CREDIT");

        await RefusedAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/wallets/{customerId}/adjust",
                new { amount = 0m, reason = StoreCreditReasons.Adjustment, note = "Nothing.", expiresAt = (DateTimeOffset?)null },
                Cancellation),
            HttpStatusCode.UnprocessableEntity);

        // Five movements later, the cache is still the sum of the ledger — which is the invariant
        // that makes the balance safe to read on every cart render instead of summing the rows.
        var derived = await Database.ScalarAsync<decimal>(
            "SELECT COALESCE(SUM(CASE WHEN type IN ('Debit', 'Expiry') "
            + "THEN -amount_amount ELSE amount_amount END), 0) "
            + "FROM pricing.wallet_transactions movement "
            + "JOIN pricing.wallets wallet ON wallet.id = movement.wallet_id "
            + "WHERE wallet.customer_id = $1",
            Cancellation,
            customerId);

        Assert.Equal(250m, derived);
        Assert.Equal(derived, await BalanceAsync(customerId));

        // And the shopper's own statement reads the same ledger, newest first.
        var statement = await ReadAsync(
            await shopper.GetAsync(new Uri("/api/v1/store/me/wallet/transactions", UriKind.Relative), Cancellation));

        Assert.Equal(3, statement.GetProperty("items").GetArrayLength());

        var wallet = await ReadAsync(
            await shopper.GetAsync(new Uri("/api/v1/store/me/wallet", UriKind.Relative), Cancellation));

        Assert.Equal(250m, PricingScenario.Amount(wallet, "balance"));
    }

    /// <summary>
    /// Every wallet route and every mover refuses while <c>pricing.store-credit</c> is off, and the
    /// balance reads as zero and inactive rather than throwing.
    /// </summary>
    /// <remarks>
    /// A deployment that has not decided its loyalty rules must not be able to accrue a liability by
    /// accident, and the flag has to hold at both doors: the routes, which answer as though the
    /// feature does not exist, and the contract, which other modules call without knowing whether it
    /// is on. Zero-and-inactive rather than an exception, because a refund handler that threw would
    /// fail the refund rather than merely decline to pay it in credit.
    /// </remarks>
    [Fact]
    public async Task Every_wallet_route_and_mover_refuses_while_store_credit_is_off()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var shopper = CreateClient();
        var customerId = await PricingScenario.ShopperAsync(shopper, Cancellation);
        var reference = Guid.CreateVersion7();

        // The shipped default is off, and this host has not been told otherwise.
        foreach (var route in new[]
                 {
                     "/api/v1/admin/wallets",
                     $"/api/v1/admin/wallets/{customerId}",
                     $"/api/v1/admin/wallets/{customerId}/transactions",
                 })
        {
            await RefusedAsync(
                await admin.GetAsync(new Uri(route, UriKind.Relative), Cancellation),
                HttpStatusCode.NotFound,
                "FEATURE_DISABLED");
        }

        await RefusedAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/wallets/{customerId}/adjust",
                new { amount = 100m, reason = StoreCreditReasons.Adjustment, note = (string?)null, expiresAt = (DateTimeOffset?)null },
                Cancellation),
            HttpStatusCode.NotFound,
            "FEATURE_DISABLED");

        foreach (var route in new[] { "/api/v1/store/me/wallet", "/api/v1/store/me/wallet/transactions" })
        {
            await RefusedAsync(
                await shopper.GetAsync(new Uri(route, UriKind.Relative), Cancellation),
                HttpStatusCode.NotFound,
                "FEATURE_DISABLED");
        }

        // The contract answers rather than throwing, and answers nothing rather than something.
        await RunOnceAsync<IStoreCredit>(async (credit, cancellation) =>
        {
            var balance = await credit.GetBalanceAsync(customerId, cancellation);

            Assert.Equal(0m, balance.Balance);
            Assert.False(balance.IsActive);
            Assert.Equal("INR", balance.CurrencyCode);

            var credited = await credit.CreditAsync(
                customerId,
                500m,
                StoreCreditReasons.Refund,
                "return",
                reference,
                cancellationToken: cancellation);

            Assert.Equal(0m, credited.Balance);
            Assert.False(credited.IsActive);

            Assert.Equal(
                0m,
                await credit.RedeemAsync(
                    customerId,
                    100m,
                    StoreCreditReasons.OrderPayment,
                    "order",
                    Guid.CreateVersion7(),
                    cancellation));
        });

        // Nothing was written at all — the refusal is a refusal, not a silent success.
        Assert.Equal(
            0,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM pricing.wallets WHERE customer_id = $1",
                Cancellation,
                customerId));

        // And a quote never applies credit that cannot exist, however much is asked for.
        var sellers = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var pricing = new PricingScenario(admin, Cancellation);

        var taxonomy = await catalogue.TaxonomyAsync();
        var seller = await sellers.ActiveAsync();
        var offer = await pricing.OfferAsync(catalogue, taxonomy, seller.Id, PricingScenario.NewHsn(), 18m, 1000.00m);

        var quoted = await pricing.QuoteAsync(
            shopper,
            [(offer.ListingId, 1)],
            walletRedeemRequested: 500m);

        Assert.Equal(0m, PricingScenario.Amount(quoted, "walletApplied"));
        Assert.Equal(1000m, PricingScenario.Amount(quoted, "amountPayable"));

        // Switched on, the very same routes and the very same call answer — which is what proves the
        // refusals above were the flag rather than a route that was never mapped.
        await using var enabled = NewFactory();

        enabled.Features[PricingFeatureFlags.StoreCredit] = true;

        using var staff = enabled.CreateClient();

        await TestSignIn.SignInAsync(
            staff,
            "admin",
            KlaraHomeSchemaFixture.BootstrapEmail,
            KlaraHomeSchemaFixture.BootstrapPassword,
            Cancellation);

        var listed = await ReadAsync(
            await staff.GetAsync(new Uri($"/api/v1/admin/wallets/{customerId}", UriKind.Relative), Cancellation));

        Assert.Equal(0m, PricingScenario.Amount(listed, "balance"));
        Assert.True(listed.GetProperty("isActive").GetBoolean());
    }

    /// <summary>Credits a wallet in its own scope and answers the failure, or null when it stood.</summary>
    /// <param name="customerId">The shopper.</param>
    /// <param name="amount">How much.</param>
    /// <param name="reference">The causing record.</param>
    private async Task<Exception?> CreditAsync(Guid customerId, decimal amount, Guid reference)
    {
        try
        {
            using var scope = Factory.Services.CreateScope();

            await scope.ServiceProvider.GetRequiredService<IStoreCredit>().CreditAsync(
                customerId,
                amount,
                StoreCreditReasons.Refund,
                "return",
                reference,
                cancellationToken: Cancellation);

            return null;
        }
        catch (DbUpdateException exception)
        {
            return exception;
        }
    }

    /// <summary>How many movements of a kind a shopper's wallet holds.</summary>
    /// <param name="customerId">The shopper.</param>
    /// <param name="type">The movement type, as the column stores it.</param>
    private Task<long> MovementsAsync(Guid customerId, string type)
        => Database.CountAsync(
            "SELECT COUNT(*) FROM pricing.wallet_transactions movement "
            + "JOIN pricing.wallets wallet ON wallet.id = movement.wallet_id "
            + "WHERE wallet.customer_id = $1 AND movement.type = $2",
            Cancellation,
            customerId,
            type);

    /// <summary>The balance cache, read from the column rather than through the service.</summary>
    /// <param name="customerId">The shopper.</param>
    private Task<decimal> BalanceAsync(Guid customerId)
        => Database.ScalarAsync<decimal>(
            "SELECT balance_amount FROM pricing.wallets WHERE customer_id = $1",
            Cancellation,
            customerId);

    /// <summary>
    /// Waits until a backend is blocked on a lock taken against a given table, rather than sleeping.
    /// </summary>
    /// <remarks>
    /// The credit has to have reached its insert before the other transaction commits, or the test
    /// proves nothing. A fixed delay would be either slow or flaky depending on the machine; the
    /// database can be asked directly, and naming the table keeps an unrelated background query from
    /// answering for it.
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

        Assert.Fail("The credit never reached the row it was meant to conflict with.");
    }
}
