using System.Net;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Accounting;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Payout batches: maker-checker in three layers, the honest adapter, and money that moves in the
/// open rather than inside one transaction.
/// </summary>
/// <remarks>
/// TEST_DEBT.md rows 243, 244, 245, 246, 247, 253 and 261.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class SettlementPayoutTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// Builds a closed, payable cycle for a fresh seller and answers it along with the seller.
    /// </summary>
    private async Task<(HttpClient Admin, OnboardedVendor Vendor, Guid CycleId, decimal NetPayable)> ClosedCycleAsync(
        HttpClient admin,
        decimal saleAmount = 5000m)
    {
        var vendor = await Sellers(admin).ActiveAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
            var cycles = scope.ServiceProvider.GetRequiredService<SettlementCycleService>();

            var period = new SettlementPeriod(DateTimeOffset.UtcNow.AddDays(-14), DateTimeOffset.UtcNow.AddDays(-1));

            var entry = LedgerEntry
                .Post(
                    vendor.Id, LedgerEntryTypes.Sale, LedgerDirection.Credit, saleAmount, "INR",
                    LedgerReferenceTypes.Manual, referenceId: null, $"sale:test:{Guid.NewGuid()}", period.Start.AddDays(1))
                .Taxed(saleAmount * 0.9m);
            context.LedgerEntries.Add(entry);
            await context.SaveChangesAsync(Cancellation);

            var closed = await cycles.CloseAsync(vendor.Id, period, closedBy: null, Cancellation);
            await context.SaveChangesAsync(Cancellation);

            return (admin, vendor, closed.Id, closed.NetPayable);
        }
    }

    /// <summary>
    /// Maker-checker holds through the HTTP surface, and the database refuses a same-user approval
    /// even when a handler is bypassed entirely.
    /// </summary>
    [Fact]
    public async Task Maker_checker_is_refused_at_the_handler_the_aggregate_and_the_database()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var (_, _, cycleId, _) = await ClosedCycleAsync(admin);

        var built = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/payout-batches", new { cycleIds = new[] { cycleId } }, Cancellation));
        var batchId = built.GetProperty("id").GetGuid();

        // Layer 1: the application handler. The same platform-admin account both built and is now
        // trying to approve the batch.
        var refused = await admin.PostAsJsonAsync(
            $"/api/v1/admin/payout-batches/{batchId}/approve", new { }, Cancellation);
        await RefusedAsync(refused, HttpStatusCode.Forbidden, "PAYOUT_SELF_APPROVAL");

        // A genuine second person succeeds through the same handler.
        var checker = await SignedInStaffAsync(admin, "finance");
        var approved = await ReadAsync(await checker.PostAsJsonAsync(
            $"/api/v1/admin/payout-batches/{batchId}/approve", new { }, Cancellation));
        Assert.Equal("Approved", approved.GetProperty("status").GetString());

        // Layer 2: the aggregate. Calling PayoutBatch.Approve directly, past the handler entirely,
        // still refuses a self-approval — a second closed cycle, batched fresh, to test it against.
        var (_, _, cycleId2, _) = await ClosedCycleAsync(admin);
        var secondBuilt = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/payout-batches", new { cycleIds = new[] { cycleId2 } }, Cancellation));

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();

            var draft = await context.PayoutBatches
                .IgnoreQueryFilters()
                .Include(batch => batch.Items)
                .SingleAsync(batch => batch.Id == secondBuilt.GetProperty("id").GetGuid(), Cancellation);

            Assert.Throws<InvalidOperationException>(
                () => draft.Approve(draft.RequestedBy!.Value, DateTimeOffset.UtcNow));
        }

        // Layer 3: the database. A raw insert naming the same user as maker and checker is refused by
        // the CHECK constraint, with no handler and no aggregate anywhere near it.
        var conflict = await Database.RefusalAsync(
            """
            INSERT INTO settlements.payout_batches
                (id, reference, status, total_amount, vendor_count, currency_code, requested_by,
                 requested_at, approved_by, approved_at, tenant_id, created_at)
            SELECT gen_random_uuid(), 'PAY-TEST-DB', 'Approved', 100, 1, 'INR', requested_by,
                   now(), requested_by, now(), tenant_id, now()
            FROM settlements.payout_batches
            WHERE id = $1
            """,
            Cancellation,
            batchId);

        Assert.Equal("23514", conflict);
    }

    /// <summary>
    /// The migration's <c>CHECK</c> constraints refuse a negative amount, an unknown entry type, a
    /// closed cycle with no <c>closed_at</c>, and a completed payout item with no
    /// <c>provider_payout_id</c> — each proved against the live schema, not against the domain
    /// classes that happen to prevent them today.
    /// </summary>
    [Fact]
    public async Task The_settlements_schema_check_constraints_refuse_what_they_are_meant_to()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync();

        const string TenantSubquery = "(SELECT tenant_id FROM vendors.vendors WHERE id = $1)";

        var negativeAmount = await Database.RefusalAsync(
            $"""
             INSERT INTO settlements.ledger_entries
                 (id, vendor_id, entry_type, direction, amount, taxable_value, currency_code,
                  reference_type, source_key, occurred_at, tenant_id, created_at)
             VALUES (gen_random_uuid(), $1, 'sale', 'Credit', -10, 0, 'INR', 'manual',
                     'ck-test-negative', now(), {TenantSubquery}, now())
             """,
            Cancellation,
            vendor.Id);
        Assert.Equal("23514", negativeAmount);

        var unknownType = await Database.RefusalAsync(
            $"""
             INSERT INTO settlements.ledger_entries
                 (id, vendor_id, entry_type, direction, amount, taxable_value, currency_code,
                  reference_type, source_key, occurred_at, tenant_id, created_at)
             VALUES (gen_random_uuid(), $1, 'not-a-real-type', 'Credit', 10, 0, 'INR', 'manual',
                     'ck-test-unknown-type', now(), {TenantSubquery}, now())
             """,
            Cancellation,
            vendor.Id);
        Assert.Equal("23514", unknownType);

        var closedWithNoClosedAt = await Database.RefusalAsync(
            $"""
             INSERT INTO settlements.settlement_cycles
                 (id, vendor_id, period_start, period_end, status, opening_balance, gross_sales,
                  taxable_sales, total_commission, total_fees, total_refunds, taxable_refunds,
                  total_adjustments, total_payouts, tcs, tds, net_payable, currency_code, entry_count,
                  closed_at, tenant_id, created_at)
             VALUES (gen_random_uuid(), $1, now() - interval '7 days', now(), 'Closed', 0, 0, 0, 0, 0,
                     0, 0, 0, 0, 0, 0, 0, 'INR', 0, NULL, {TenantSubquery}, now())
             """,
            Cancellation,
            vendor.Id);
        Assert.Equal("23514", closedWithNoClosedAt);

        var completedWithNoProviderId = await Database.RefusalAsync(
            $"""
             INSERT INTO settlements.payout_batches
                 (id, reference, status, total_amount, vendor_count, currency_code, requested_at,
                  tenant_id, created_at)
             VALUES (gen_random_uuid(), 'PAY-TEST-CK', 'Draft', 0, 0, 'INR', now(), {TenantSubquery}, now())
             """,
            Cancellation,
            vendor.Id);
        Assert.Null(completedWithNoProviderId);

        var batchRow = await Database.ScalarAsync<Guid>(
            "SELECT id FROM settlements.payout_batches WHERE reference = 'PAY-TEST-CK'", Cancellation);

        var completedItem = await Database.RefusalAsync(
            $"""
             INSERT INTO settlements.payout_items
                 (id, payout_batch_id, vendor_id, amount, currency_code, status, tenant_id, created_at)
             VALUES (gen_random_uuid(), $2, $1, 100, 'INR', 'Completed', {TenantSubquery}, now())
             """,
            Cancellation,
            vendor.Id,
            batchRow);
        Assert.Equal("23514", completedItem);
    }

    /// <summary>
    /// With no payout rail configured — the deployment's default, by the User's own instruction — a
    /// batch builds and approves exactly as it would with one, and only sending is refused, honestly.
    /// </summary>
    [Fact]
    public async Task With_no_provider_configured_everything_up_to_sending_still_works()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var (_, _, cycleId, netPayable) = await ClosedCycleAsync(admin);

        Assert.True(netPayable > 0m);

        var built = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/payout-batches", new { cycleIds = new[] { cycleId } }, Cancellation));
        Assert.Equal("Draft", built.GetProperty("status").GetString());

        var checker = await SignedInStaffAsync(admin, "finance");
        var approved = await ReadAsync(await checker.PostAsJsonAsync(
            $"/api/v1/admin/payout-batches/{built.GetProperty("id").GetGuid()}/approve", new { }, Cancellation));
        Assert.Equal("Approved", approved.GetProperty("status").GetString());

        var refused = await admin.PostAsJsonAsync(
            $"/api/v1/admin/payout-batches/{approved.GetProperty("id").GetGuid()}/process", new { }, Cancellation);

        await RefusedAsync(refused, HttpStatusCode.ServiceUnavailable, "PAYOUT_PROVIDER_UNAVAILABLE");

        // The batch itself is untouched by the refusal: still approved, still worth what it was.
        var reread = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/payout-batches/{approved.GetProperty("id").GetGuid()}", UriKind.Relative),
            Cancellation));

        Assert.Equal("Approved", reread.GetProperty("status").GetString());
        Assert.Equal(netPayable, reread.GetProperty("totalAmount").GetDecimal());
    }

    /// <summary>
    /// A batch survives a partial send: a completed transfer posts a debit and marks its cycle paid;
    /// a failed one posts nothing and releases its cycle to be paid in a new batch — and calling
    /// <c>/process</c> again continues rather than re-sending what already went.
    /// </summary>
    [Fact]
    public async Task A_batch_survives_a_partial_send_and_process_is_resumable()
    {
        SkipWithoutDocker();

        Factory.Overrides["Payouts:Provider"] = FakePayoutProvider.Name;

        var admin = await SignedInAdministratorAsync();

        var (_, vendorA, cycleA, netA) = await ClosedCycleAsync(admin, saleAmount: 3000m);
        var (_, vendorB, cycleB, _) = await ClosedCycleAsync(admin, saleAmount: 4000m);

        await Database.ExecuteAsync(
            "UPDATE vendors.vendors SET gateway_account_id = 'acc_a' WHERE id = $1", Cancellation, vendorA.Id);
        await Database.ExecuteAsync(
            "UPDATE vendors.vendors SET gateway_account_id = 'acc_b' WHERE id = $1", Cancellation, vendorB.Id);

        Factory.Payouts.Outcomes[vendorA.Id] = PayoutOutcome.Completed;
        Factory.Payouts.Outcomes[vendorB.Id] = PayoutOutcome.Failed;

        var built = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/payout-batches", new { cycleIds = new[] { cycleA, cycleB } }, Cancellation));
        var batchId = built.GetProperty("id").GetGuid();

        var checker = await SignedInStaffAsync(admin, "finance");
        await ReadAsync(await checker.PostAsJsonAsync(
            $"/api/v1/admin/payout-batches/{batchId}/approve", new { }, Cancellation));

        var processed = await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/payout-batches/{batchId}/process", new { }, Cancellation));

        Assert.Equal("PartiallyFailed", processed.GetProperty("status").GetString());

        var items = processed.GetProperty("items").EnumerateArray().ToArray();
        var itemA = items.Single(item => item.GetProperty("vendorId").GetGuid() == vendorA.Id);
        var itemB = items.Single(item => item.GetProperty("vendorId").GetGuid() == vendorB.Id);

        Assert.Equal("Completed", itemA.GetProperty("status").GetString());
        Assert.NotNull(itemA.GetProperty("providerPayoutId").GetString());
        Assert.Equal("Failed", itemB.GetProperty("status").GetString());
        Assert.NotNull(itemB.GetProperty("failureReason").GetString());

        // Calling process again does not resend the completed transfer: exactly one send was ever
        // recorded against it.
        var resent = await admin.PostAsJsonAsync(
            $"/api/v1/admin/payout-batches/{batchId}/process", new { }, Cancellation);
        await RefusedAsync(resent, HttpStatusCode.Conflict, "PAYOUT_BATCH_STATE");

        Assert.Single(Factory.Payouts.Sent, request => request.VendorId == vendorA.Id);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();

        var debit = await context.LedgerEntries
            .AsNoTracking().IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                entry => entry.VendorId == vendorA.Id && entry.EntryType == LedgerEntryTypes.Payout, Cancellation);

        Assert.NotNull(debit);
        Assert.Equal(netA, debit!.Amount);

        var noDebitForFailure = await context.LedgerEntries
            .AsNoTracking().IgnoreQueryFilters()
            .AnyAsync(entry => entry.VendorId == vendorB.Id && entry.EntryType == LedgerEntryTypes.Payout, Cancellation);
        Assert.False(noDebitForFailure);

        var cycleARow = await context.Cycles.AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(cycle => cycle.Id == cycleA, Cancellation);
        var cycleBRow = await context.Cycles.AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(cycle => cycle.Id == cycleB, Cancellation);

        Assert.Equal(SettlementCycleStatus.Paid, cycleARow.Status);
        Assert.Equal(SettlementCycleStatus.Closed, cycleBRow.Status);
        Assert.Null(cycleBRow.PayoutBatchId);
    }

    /// <summary>
    /// Two concurrent batch builds take two consecutive references, gaplessly.
    /// </summary>
    [Fact]
    public async Task Payout_references_are_gapless_and_consecutive()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var (_, _, cycle1, _) = await ClosedCycleAsync(admin, saleAmount: 500m);
        var (_, _, cycle2, _) = await ClosedCycleAsync(admin, saleAmount: 600m);

        var first = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/payout-batches", new { cycleIds = new[] { cycle1 } }, Cancellation));
        var second = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/payout-batches", new { cycleIds = new[] { cycle2 } }, Cancellation));

        var firstReference = first.GetProperty("reference").GetString()!;
        var secondReference = second.GetProperty("reference").GetString()!;

        var firstNumber = int.Parse(firstReference[(firstReference.LastIndexOf('-') + 1)..]);
        var secondNumber = int.Parse(secondReference[(secondReference.LastIndexOf('-') + 1)..]);

        Assert.Equal(firstNumber + 1, secondNumber);
    }

    /// <summary>
    /// The reconciliation sweep reports a transfer still in flight without touching it, and applies
    /// the gateway's own answer once it resolves — repair, never a guess born of the clock.
    /// </summary>
    [Fact]
    public async Task Reconciliation_reports_a_stuck_transfer_and_repairs_one_that_has_resolved()
    {
        SkipWithoutDocker();

        Factory.Overrides["Payouts:Provider"] = FakePayoutProvider.Name;

        var admin = await SignedInAdministratorAsync();
        var (_, vendor, cycleId, netPayable) = await ClosedCycleAsync(admin, saleAmount: 2000m);

        await Database.ExecuteAsync(
            "UPDATE vendors.vendors SET gateway_account_id = 'acc_stuck' WHERE id = $1", Cancellation, vendor.Id);

        Factory.Payouts.Outcomes[vendor.Id] = PayoutOutcome.StillMoving;

        var built = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/payout-batches", new { cycleIds = new[] { cycleId } }, Cancellation));
        var checker = await SignedInStaffAsync(admin, "finance");
        await ReadAsync(await checker.PostAsJsonAsync(
            $"/api/v1/admin/payout-batches/{built.GetProperty("id").GetGuid()}/approve", new { }, Cancellation));

        var processed = await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/payout-batches/{built.GetProperty("id").GetGuid()}/process", new { }, Cancellation));

        // Still queued at the gateway, so the batch itself has not finished.
        Assert.Equal("Processing", processed.GetProperty("status").GetString());
        var item = Assert.Single(processed.GetProperty("items").EnumerateArray());
        var providerPayoutId = item.GetProperty("providerPayoutId").GetString()!;

        using (var scope = Factory.Services.CreateScope())
        {
            var workflow = scope.ServiceProvider
                .GetRequiredService<KlaraHome.Modules.Settlements.Infrastructure.Payouts.PayoutWorkflow>();
            var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
            var registry = scope.ServiceProvider
                .GetRequiredService<KlaraHome.Modules.Settlements.Infrastructure.Payouts.PayoutProviderRegistry>();

            var batch = await context.PayoutBatches.IgnoreQueryFilters()
                .Include(candidate => candidate.Items)
                .SingleAsync(candidate => candidate.Id == built.GetProperty("id").GetGuid(), Cancellation);
            var pendingItem = batch.Items.Single();

            // The sweep's own rule: still moving, and asked again — the answer is re-read but the
            // item is not touched, because declaring it failed for being slow would free the cycle
            // to be paid a second time.
            var rail = registry.For(batch.Provider);
            var stillMoving = await rail.FetchAsync(providerPayoutId, Cancellation);
            Assert.True(stillMoving.IsSuccess);
            Assert.False(stillMoving.Value.IsProcessed);
            Assert.False(stillMoving.Value.IsFailed);

            await context.Entry(pendingItem).ReloadAsync(Cancellation);
            Assert.Equal(PayoutItemStatus.Processing, pendingItem.Status);

            // The gateway has since resolved it. The sweep's mechanism — apply, then settle — repairs
            // it exactly as the fifteen-minute worker would.
            Factory.Payouts.Reanswer(
                providerPayoutId,
                new KlaraHome.Modules.Settlements.Infrastructure.Payouts.ProviderPayout(
                    providerPayoutId, "processed", IsProcessed: true, IsFailed: false, "UTR-REPAIRED", null,
                    DateTimeOffset.UtcNow));

            var resolved = await rail.FetchAsync(providerPayoutId, Cancellation);
            await workflow.ApplyAsync(batch, pendingItem, resolved.Value, Cancellation);
            await workflow.SettleAsync(batch, Cancellation);
        }

        var reread = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/payout-batches/{built.GetProperty("id").GetGuid()}", UriKind.Relative),
            Cancellation));

        Assert.Equal("Completed", reread.GetProperty("status").GetString());
        Assert.Equal(netPayable, reread.GetProperty("settledAmount").GetDecimal());
    }
}
