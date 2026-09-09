using KlaraHome.Contracts.Platform;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Accounting;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The settlement cycle: what it sweeps, what it owes, and the two statutory deductions it fixes.
/// </summary>
/// <remarks>
/// TEST_DEBT.md rows 238, 241, 242, 252 and 262. Every test posts <see cref="LedgerEntry"/> rows
/// directly — Settlements' own domain entity, not another module's — and then closes them through
/// the real, unmodified <c>SettlementCycleService</c> resolved from the host's own container. Nothing
/// here needs <c>IOrderSettlement</c>: the cycle service reads only what is already on the ledger.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class SettlementCycleTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// TCS is taken on the net value of taxable supplies; TDS is taken on the gross amount including
    /// GST — two different figures on the same closed period, not one figure used twice.
    /// </summary>
    [Fact]
    public async Task Closing_a_period_computes_TCS_and_TDS_on_their_own_different_bases()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        // Settings are a store-wide document shared by every test in this collection, so a rate this
        // test depends on is pinned explicitly rather than trusted to whatever an earlier test in the
        // run last left it as.
        await ReadAsync(await admin.PutAsJsonAsync(
            "/api/v1/admin/settings/settlements",
            new
            {
                frequency = "weekly",
                weekStartDay = 1,
                holdDays = 7,
                minimumPayoutAmount = 100m,
                payoutApprovalThreshold = 0m,
                platformFeePercent = 0m,
                platformFeeFixed = 0m,
                platformServiceGstRate = 18m,
                paymentGatewayFeePercent = 0m,
                chargeGatewayFeeToVendor = false,
                chargeShippingToVendor = true,
                tcsEnabled = true,
                tcsRatePercent = 0.5m,
                tdsEnabled = true,
                tdsRatePercent = 0.1m,
                tdsRateWithoutPanPercent = 5m,
                tdsAnnualThreshold = 0m,
                autoBatchOnClose = false,
            },
            Cancellation));

        var vendor = await Sellers(admin).ActiveAsync();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
        var cycles = scope.ServiceProvider.GetRequiredService<SettlementCycleService>();
        var settings = scope.ServiceProvider.GetRequiredService<IStoreSettings>();

        var period = new SettlementPeriod(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));

        // Gross (inclusive of GST) 1180, taxable value (net of GST) 1000 — the two bases genuinely
        // differ, which is the only way this test can fail for the right reason.
        Post(context, vendor.Id, LedgerEntryTypes.Sale, LedgerDirection.Credit, 1180m, 1000m, period.Start.AddDays(1));

        await context.SaveChangesAsync(Cancellation);

        var closed = await cycles.CloseAsync(vendor.Id, period, closedBy: null, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        var policy = await settings.GetAsync<SettlementSettings>(Cancellation);

        var expectedTcs = SettlementCalculator.Round(1000m * policy.TcsRatePercent / 100m);
        var expectedTds = SettlementCalculator.Round(1180m * policy.TdsRatePercent / 100m);

        Assert.Equal(expectedTcs, closed.Tcs);
        Assert.Equal(expectedTds, closed.Tds);
        Assert.NotEqual(closed.Tcs, closed.Tds);

        // And the extract reads them off the closed cycle rather than recomputing — the API's own
        // report agrees with what CloseAsync fixed.
        var extract = await ReadAsync(await admin.GetAsync(
            new Uri(
                $"/api/v1/admin/reports/tcs-tds?vendorId={vendor.Id}"
                + $"&from={Uri.EscapeDataString(period.Start.ToString("O"))}"
                + $"&to={Uri.EscapeDataString(period.End.ToString("O"))}",
                UriKind.Relative),
            Cancellation));

        var row = Assert.Single(extract.GetProperty("rows").EnumerateArray());
        Assert.Equal(expectedTcs, row.GetProperty("tcs").GetDecimal());
        Assert.Equal(expectedTds, row.GetProperty("tds").GetDecimal());
        Assert.NotEqual(row.GetProperty("netTaxableSupplies").GetDecimal(), row.GetProperty("netGrossSales").GetDecimal());
    }

    /// <summary>
    /// A cycle draws in every unassigned entry older than its end, including one that predates the
    /// period entirely, and every entry lands in exactly one cycle.
    /// </summary>
    [Fact]
    public async Task A_cycle_sweeps_every_unassigned_entry_older_than_its_end_into_exactly_one_cycle()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
        var cycles = scope.ServiceProvider.GetRequiredService<SettlementCycleService>();

        var period = new SettlementPeriod(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow.AddDays(-1));

        // Posted long before this period even opened — a credit note against a much older sale,
        // arriving late.
        var late = Post(
            context, vendor.Id, LedgerEntryTypes.Adjustment, LedgerDirection.Credit, 50m, 0m,
            period.Start.AddDays(-90));

        // Squarely inside the period.
        var inside = Post(
            context, vendor.Id, LedgerEntryTypes.Adjustment, LedgerDirection.Credit, 25m, 0m,
            period.Start.AddDays(2));

        await context.SaveChangesAsync(Cancellation);

        var closed = await cycles.CloseAsync(vendor.Id, period, closedBy: null, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        await context.Entry(late).ReloadAsync(Cancellation);
        await context.Entry(inside).ReloadAsync(Cancellation);

        Assert.Equal(closed.Id, late.SettlementCycleId);
        Assert.Equal(closed.Id, inside.SettlementCycleId);
        Assert.Equal(75m, closed.TotalAdjustments);

        // A second period cannot claim either entry a second time: they are no longer unassigned.
        var nextPeriod = new SettlementPeriod(period.End, period.End.AddDays(7));
        var nextClosed = await cycles.CloseAsync(vendor.Id, nextPeriod, closedBy: null, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        Assert.Equal(0, nextClosed.EntryCount);
    }

    /// <summary>
    /// A cycle's opening balance is the signed sum over entries already assigned to a cycle, and it
    /// survives a payout that failed: the money the failed batch never sent is still owed in the next
    /// period.
    /// </summary>
    [Fact]
    public async Task Opening_balance_is_the_settled_sum_and_survives_a_failed_payout()
    {
        SkipWithoutDocker();

        // Must be set before the host is first created, because a payout provider is selected from
        // configuration read at start-up.
        Factory.Overrides["Payouts:Provider"] = FakePayoutProvider.Name;

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
        var cycles = scope.ServiceProvider.GetRequiredService<SettlementCycleService>();

        var period1 = new SettlementPeriod(DateTimeOffset.UtcNow.AddDays(-14), DateTimeOffset.UtcNow.AddDays(-7));
        Post(context, vendor.Id, LedgerEntryTypes.Sale, LedgerDirection.Credit, 500m, 450m, period1.Start.AddDays(1));
        await context.SaveChangesAsync(Cancellation);

        var closed1 = await cycles.CloseAsync(vendor.Id, period1, closedBy: null, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        Assert.Equal(0m, closed1.OpeningBalance);
        Assert.True(closed1.NetPayable > 0m);

        // A gateway account so the item is actually sent to the rail rather than skipped for want of
        // one — IVendorPayoutAccounts is its own, separately parked gap (docs/PARKING_LOT.md); this
        // is the one place this suite reaches past it, deliberately, to exercise what happens once it
        // is filled in.
        await Database.ExecuteAsync(
            "UPDATE vendors.vendors SET gateway_account_id = 'acc_test' WHERE id = $1",
            Cancellation,
            vendor.Id);

        // Build, approve and process a batch through the fake rail, failing this seller's transfer
        // deliberately — the honest way to fail a real transfer, not a hand-edited row.
        Factory.Payouts.Outcomes[vendor.Id] = PayoutOutcome.Failed;

        var built = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/payout-batches",
            new { cycleIds = new[] { closed1.Id } },
            Cancellation));

        var checker = await SignedInStaffAsync(admin, "finance");
        var approved = await ReadAsync(await checker.PostAsJsonAsync(
            $"/api/v1/admin/payout-batches/{built.GetProperty("id").GetGuid()}/approve", new { }, Cancellation));

        var processed = await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/payout-batches/{approved.GetProperty("id").GetGuid()}/process", new { }, Cancellation));

        Assert.Equal("Failed", processed.GetProperty("status").GetString());

        // The cycle is released — it stands as closed but unpaid — and a second period opens owing
        // exactly what the first one still owes.
        var period2 = new SettlementPeriod(period1.End, period1.End.AddDays(7));
        var closed2 = await cycles.CloseAsync(vendor.Id, period2, closedBy: null, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        Assert.Equal(closed1.NetPayable, closed2.OpeningBalance);
    }

    /// <summary>
    /// A period cannot be closed before its return hold expires, and closing it with the override
    /// bypasses exactly that check and nothing else.
    /// </summary>
    [Fact]
    public async Task A_period_is_refused_before_its_hold_expires_and_closes_once_forced()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync();

        // A 90-day hold puts "the previous period" — whatever it is right now — nowhere near
        // closable, without needing to control the clock.
        await ReadAsync(await admin.PutAsJsonAsync(
            "/api/v1/admin/settings/settlements",
            new
            {
                frequency = "weekly",
                weekStartDay = 1,
                holdDays = 90,
                minimumPayoutAmount = 0m,
                payoutApprovalThreshold = 0m,
                platformFeePercent = 0m,
                platformFeeFixed = 0m,
                platformServiceGstRate = 18m,
                paymentGatewayFeePercent = 0m,
                chargeGatewayFeeToVendor = false,
                chargeShippingToVendor = true,
                tcsEnabled = true,
                tcsRatePercent = 0.5m,
                tdsEnabled = true,
                tdsRatePercent = 0.1m,
                tdsRateWithoutPanPercent = 5m,
                tdsAnnualThreshold = 0m,
                autoBatchOnClose = false,
            },
            Cancellation));

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
            Post(context, vendor.Id, LedgerEntryTypes.Adjustment, LedgerDirection.Credit, 10m, 0m,
                DateTimeOffset.UtcNow.AddDays(-8));
            await context.SaveChangesAsync(Cancellation);
        }

        var refused = await admin.PostAsJsonAsync(
            "/api/v1/admin/settlements/cycles/close",
            new { vendorId = vendor.Id, force = false },
            Cancellation);

        await RefusedAsync(refused, System.Net.HttpStatusCode.Conflict, "SETTLEMENT_CYCLE_NOT_DUE");

        var forced = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/settlements/cycles/close",
            new { vendorId = vendor.Id, force = true },
            Cancellation));

        Assert.Equal("Closed", forced.GetProperty("status").GetString());
    }

    /// <summary>
    /// The scheduler closes the previous period once and only once for a settleable seller, and is
    /// safe to run twice concurrently against the same row.
    /// </summary>
    [Fact]
    public async Task Closing_the_same_period_twice_is_a_no_op_the_second_time()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
        var cycles = scope.ServiceProvider.GetRequiredService<SettlementCycleService>();

        var period = new SettlementPeriod(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow.AddDays(-1));
        Post(context, vendor.Id, LedgerEntryTypes.Sale, LedgerDirection.Credit, 200m, 180m, period.Start.AddDays(1));
        await context.SaveChangesAsync(Cancellation);

        var first = await cycles.CloseAsync(vendor.Id, period, closedBy: null, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        // The unique index on (tenant, vendor, period_start) is what makes this safe against a
        // concurrent scheduler pass, not a lock this test can hold open — so two sequential calls
        // that land on an already-closed row are the observable half of that guarantee.
        var second = await cycles.CloseAsync(vendor.Id, period, closedBy: null, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.NetPayable, second.NetPayable);

        var count = await context.Cycles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(cycle => cycle.VendorId == vendor.Id && cycle.PeriodStart == period.Start, Cancellation);

        Assert.Equal(1, count);
    }

    /// <summary>
    /// The TDS annual threshold is evaluated on the financial year to date across cycles, and a
    /// seller who crosses it mid-year is deducted from that period on, not retrospectively.
    /// </summary>
    [Fact]
    public async Task TDS_annual_threshold_is_evaluated_across_cycles_and_applies_only_from_the_crossing_period()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync();

        await ReadAsync(await admin.PutAsJsonAsync(
            "/api/v1/admin/settings/settlements",
            new
            {
                frequency = "weekly",
                weekStartDay = 1,
                holdDays = 0,
                minimumPayoutAmount = 0m,
                payoutApprovalThreshold = 0m,
                platformFeePercent = 0m,
                platformFeeFixed = 0m,
                platformServiceGstRate = 18m,
                paymentGatewayFeePercent = 0m,
                chargeGatewayFeeToVendor = false,
                chargeShippingToVendor = true,
                tcsEnabled = false,
                tcsRatePercent = 0.5m,
                tdsEnabled = true,
                tdsRatePercent = 1m,
                tdsRateWithoutPanPercent = 5m,
                tdsAnnualThreshold = 1000m,
                autoBatchOnClose = false,
            },
            Cancellation));

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
        var cycles = scope.ServiceProvider.GetRequiredService<SettlementCycleService>();

        // A financial year starts on 1 April in India; anchor both periods inside the current one so
        // "year to date" has a stable start regardless of when this test runs.
        var yearAnchor = DateTimeOffset.UtcNow.Month is >= 4 ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow.AddMonths(-1);
        var period1 = new SettlementPeriod(yearAnchor.AddDays(-14), yearAnchor.AddDays(-7));
        var period2 = new SettlementPeriod(period1.End, period1.End.AddDays(7));

        // First period: 800 gross, under the 1000 threshold on its own.
        Post(context, vendor.Id, LedgerEntryTypes.Sale, LedgerDirection.Credit, 800m, 800m, period1.Start.AddDays(1));
        await context.SaveChangesAsync(Cancellation);

        var closed1 = await cycles.CloseAsync(vendor.Id, period1, closedBy: null, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        Assert.Equal(0m, closed1.Tds);

        // Second period: another 800. The year to date, 1600, has now crossed 1000 — TDS applies
        // from here, not retrospectively against the first period.
        Post(context, vendor.Id, LedgerEntryTypes.Sale, LedgerDirection.Credit, 800m, 800m, period2.Start.AddDays(1));
        await context.SaveChangesAsync(Cancellation);

        var closed2 = await cycles.CloseAsync(vendor.Id, period2, closedBy: null, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        Assert.True(closed2.Tds > 0m);

        await context.Entry(closed1).ReloadAsync(Cancellation);
        Assert.Equal(0m, closed1.Tds);
    }

    /// <summary>
    /// <c>Settlements.CycleClosed</c> is written to the outbox in the same transaction as the cycle
    /// it describes, from the keyed per-context outbox — not after it, and not from a second one.
    /// </summary>
    [Fact]
    public async Task Closing_a_period_writes_the_cycle_closed_event_in_the_same_transaction()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
        var cycles = scope.ServiceProvider.GetRequiredService<SettlementCycleService>();

        var period = new SettlementPeriod(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow.AddDays(-1));
        Post(context, vendor.Id, LedgerEntryTypes.Sale, LedgerDirection.Credit, 300m, 270m, period.Start.AddDays(1));

        // Nothing is saved yet: the cycle and its outbox message both live only in this context's
        // pending change set, which is the whole of what "same transaction" means here.
        var closed = await cycles.CloseAsync(vendor.Id, period, closedBy: null, Cancellation);

        var beforeSave = await Database.CountAsync(
            "SELECT COUNT(*) FROM platform.outbox_messages WHERE type ILIKE '%SettlementCycleClosed%' "
            + "AND payload::text ILIKE $1",
            Cancellation,
            $"%{closed.Id}%");
        Assert.Equal(0, beforeSave);

        await context.SaveChangesAsync(Cancellation);

        var afterSave = await Database.CountAsync(
            "SELECT COUNT(*) FROM platform.outbox_messages WHERE type ILIKE '%SettlementCycleClosed%' "
            + "AND payload::text ILIKE $1",
            Cancellation,
            $"%{closed.Id}%");
        Assert.Equal(1, afterSave);
    }

    /// <summary>Posts a directly-constructed ledger entry, bypassing the poster.</summary>
    private static LedgerEntry Post(
        SettlementsDbContext context,
        Guid vendorId,
        string entryType,
        LedgerDirection direction,
        decimal amount,
        decimal taxableValue,
        DateTimeOffset occurredAt)
    {
        var entry = LedgerEntry
            .Post(
                vendorId,
                entryType,
                direction,
                amount,
                "INR",
                LedgerReferenceTypes.Manual,
                referenceId: null,
                sourceKey: $"{entryType}:test:{Guid.NewGuid()}",
                occurredAt)
            .Taxed(taxableValue);

        context.LedgerEntries.Add(entry);
        return entry;
    }
}
