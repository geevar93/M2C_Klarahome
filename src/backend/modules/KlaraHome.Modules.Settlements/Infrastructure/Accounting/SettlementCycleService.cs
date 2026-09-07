using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Events;
using KlaraHome.Modules.Settlements.Infrastructure.Invoicing;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Settlements.Infrastructure.Accounting;

/// <summary>
/// Draws the line under a seller's period.
/// </summary>
/// <remarks>
/// <para>
/// The one place a cycle is opened or closed. An operator's button, the scheduler and a repair script
/// all come through here, so none of them can total a period differently from the others.
/// </para>
/// <para>
/// <b>A cycle draws in every unassigned entry that occurred before its end, not only those inside
/// it.</b> The difference matters: an entry posted late — a credit note against a sale from two
/// periods ago, a payout that completed after its cycle closed — would otherwise belong to a period
/// that is already frozen and be settled by nobody. Sweeping everything older than the period end
/// means every entry is drawn into exactly one cycle, which is the invariant the whole ledger rests
/// on.
/// </para>
/// <para>
/// <b>The opening balance is the sum of everything already settled.</b> Not the previous cycle's net
/// payable, which would drift the moment a payout failed, and not a stored running total, which would
/// be a second copy of the ledger. It is <c>Σ credits − Σ debits</c> over the entries that carry a
/// cycle id, which by construction is what was owed and not yet paid at the moment this period began.
/// </para>
/// </remarks>
/// <param name="context">The Settlements data context.</param>
/// <param name="poster">The one place the ledger is written to.</param>
/// <param name="vendors">Reads the seller's PAN, which decides the TDS rate.</param>
/// <param name="settings">Supplies the store's settlement policy.</param>
/// <param name="invoices">Raises the platform's own tax invoice for the commission the cycle charged.</param>
/// <param name="events">Announces a closed period.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was closed and what was skipped.</param>
internal sealed partial class SettlementCycleService(
    SettlementsDbContext context,
    SettlementPoster poster,
    IVendorPayouts vendors,
    IStoreSettings settings,
    CommissionInvoiceService invoices,
    SettlementsEventPublisher events,
    IClock clock,
    ILogger<SettlementCycleService> logger)
{
    /// <summary>
    /// The sellers with money on the ledger that no cycle has drawn in yet.
    /// </summary>
    /// <remarks>
    /// The scheduler's worklist, and it is derived from the ledger rather than from the seller list.
    /// A marketplace with four thousand sellers has a few hundred with anything to settle in any given
    /// week, and opening an empty cycle for the rest would be four thousand rows a week saying nothing
    /// happened.
    /// </remarks>
    /// <param name="before">Only entries that occurred before this instant.</param>
    /// <param name="limit">How many sellers to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<Guid>> SettleableVendorsAsync(
        DateTimeOffset before,
        int limit,
        CancellationToken cancellationToken)
        => await context.LedgerEntries
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(entry => entry.TenantId == context.TenantId
                            && entry.SettlementCycleId == null
                            && entry.OccurredAt < before
                            && entry.VendorId != null)
            .Select(entry => entry.VendorId!.Value)
            .Distinct()
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Closes one seller's period, opening the cycle for it if nobody has yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Idempotent by the unique index on <c>(tenant, vendor, period_start)</c>: a cycle that already
    /// exists and is already closed is returned untouched rather than closed a second time. That is
    /// what makes the scheduler safe to run twice and safe to run beside an operator pressing the
    /// button.
    /// </para>
    /// <para>
    /// It writes and does not commit. The caller's <c>SaveChangesAsync</c> is what makes the closing
    /// real, so a period that fails halfway through closes not at all rather than half.
    /// </para>
    /// </remarks>
    /// <param name="vendorId">The seller.</param>
    /// <param name="period">The period being closed.</param>
    /// <param name="closedBy">Who is closing it, or null for the scheduler.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SettlementCycle> CloseAsync(
        Guid vendorId,
        SettlementPeriod period,
        Guid? closedBy,
        CancellationToken cancellationToken)
    {
        var policy = await settings.GetAsync<SettlementSettings>(cancellationToken).ConfigureAwait(false);

        var cycle = await context.Cycles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == context.TenantId
                             && candidate.VendorId == vendorId
                             && candidate.PeriodStart == period.Start,
                cancellationToken)
            .ConfigureAwait(false);

        if (cycle is { Status: not SettlementCycleStatus.Open })
        {
            return cycle;
        }

        var openingBalance = await OpeningBalanceAsync(vendorId, cancellationToken).ConfigureAwait(false);

        cycle ??= Track(SettlementCycle.Open(vendorId, period.Start, period.End, openingBalance, Money.Inr));

        var entries = await context.LedgerEntries
            .IgnoreQueryFilters()
            .Where(entry => entry.TenantId == context.TenantId
                            && entry.VendorId == vendorId
                            && entry.SettlementCycleId == null
                            && entry.OccurredAt < period.End)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var entry in entries)
        {
            entry.AssignTo(cycle.Id);
        }

        var totals = Total(entries);
        var deduction = await DeductAsync(vendorId, period, totals, policy, cancellationToken)
            .ConfigureAwait(false);

        var taxEntries = poster.PostDeductions(vendorId, cycle.Id, deduction, cycle.CurrencyCode, clock.UtcNow);

        cycle.Close(totals, deduction.Tcs, deduction.Tds, entries.Count + taxEntries.Count, clock.UtcNow, closedBy);

        // The platform's own tax invoice for what it charged in this period. Raised here because a
        // cycle is already the period the charges are agreed over, and inventing a second period for
        // the invoice would give two answers to what a seller was charged in January. It writes and
        // does not commit, on the same terms as everything else in this method.
        await invoices.RaiseAsync(cycle, cancellationToken).ConfigureAwait(false);

        events.CycleClosed(cycle);

        CycleClosed(logger, vendorId, period.Start, period.End, cycle.NetPayable);

        return cycle;
    }

    /// <summary>What a seller is owed right now, over every entry on their account.</summary>
    /// <remarks>
    /// The live balance, as opposed to a closed cycle's frozen figure. It is what a seller's dashboard
    /// shows and what a support call answers, and it is a sum rather than a column for the reason the
    /// whole ledger is append-only: a stored balance can disagree with its own history.
    /// </remarks>
    /// <param name="vendorId">The seller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<decimal> BalanceAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        var sums = await context.LedgerEntries
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(entry => entry.TenantId == context.TenantId && entry.VendorId == vendorId)
            .GroupBy(entry => entry.Direction)
            .Select(group => new { Direction = group.Key, Amount = group.Sum(entry => entry.Amount) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Signed(sums.Select(sum => (sum.Direction, sum.Amount)));
    }

    /// <summary>
    /// What was owed at the moment this period began: the balance over everything already settled.
    /// </summary>
    /// <remarks>
    /// Deliberately not "the previous cycle's net payable". That figure was right when it was written
    /// and stops being right the moment a payout against it fails, and carrying it forward would
    /// forget the failure. The sum over settled entries cannot forget anything.
    /// </remarks>
    private async Task<decimal> OpeningBalanceAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        var sums = await context.LedgerEntries
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(entry => entry.TenantId == context.TenantId
                            && entry.VendorId == vendorId
                            && entry.SettlementCycleId != null)
            .GroupBy(entry => entry.Direction)
            .Select(group => new { Direction = group.Key, Amount = group.Sum(entry => entry.Amount) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Signed(sums.Select(sum => (sum.Direction, sum.Amount)));
    }

    /// <summary>
    /// What the state takes out of the period.
    /// </summary>
    /// <remarks>
    /// The seller's PAN status and their running total for the financial year both come from outside
    /// this period, which is why the deduction cannot be computed from the totals alone. The year to
    /// date is read from the seller's own closed cycles rather than from the ledger, because a cycle
    /// records what was actually declared and a re-summed ledger would drift from it the first time
    /// somebody posted an adjustment against a closed period.
    /// </remarks>
    private async Task<StatutoryDeduction> DeductAsync(
        Guid vendorId,
        SettlementPeriod period,
        SettlementTotals totals,
        SettlementSettings policy,
        CancellationToken cancellationToken)
    {
        var yearStart = FinancialYear.StartOf(period.End);

        var priorGross = await context.Cycles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(cycle => cycle.TenantId == context.TenantId
                            && cycle.VendorId == vendorId
                            && cycle.Status != SettlementCycleStatus.Open
                            && cycle.PeriodEnd > yearStart
                            && cycle.PeriodEnd <= period.End)
            .SumAsync(cycle => cycle.GrossSales - cycle.TotalRefunds, cancellationToken)
            .ConfigureAwait(false);

        var profile = await vendors.FindAsync(vendorId, cancellationToken).ConfigureAwait(false);
        var hasPan = !string.IsNullOrWhiteSpace(profile?.Pan);

        return StatutoryDeductions.For(
            totals.NetTaxableSupplies,
            totals.NetGrossSales,
            priorGross + totals.NetGrossSales,
            hasPan,
            policy);
    }

    /// <summary>
    /// Adds the drawn-in entries up, by type and in total.
    /// </summary>
    /// <remarks>
    /// One pass rather than eleven queries. The itemised figures are what a statement shows; the net
    /// movement is what the net payable is computed from, and it is summed over every entry rather
    /// than assembled from the itemised ones — so an entry type nobody thought to itemise is still in
    /// the money.
    /// </remarks>
    private static SettlementTotals Total(List<LedgerEntry> entries)
    {
        if (entries.Count == 0)
        {
            return SettlementTotals.Empty;
        }

        var grossSales = 0m;
        var taxableSales = 0m;
        var commission = 0m;
        var fees = 0m;
        var refunds = 0m;
        var taxableRefunds = 0m;
        var adjustments = 0m;
        var payouts = 0m;
        var movement = 0m;

        foreach (var entry in entries)
        {
            movement += entry.SignedAmount;

            switch (entry.EntryType)
            {
                case LedgerEntryTypes.Sale:
                    grossSales += entry.Amount;
                    taxableSales += entry.TaxableValue;
                    break;

                case LedgerEntryTypes.Commission:
                case LedgerEntryTypes.PlatformTax:
                    commission += entry.Amount;
                    break;

                case LedgerEntryTypes.PlatformFee:
                case LedgerEntryTypes.PaymentFee:
                case LedgerEntryTypes.ShippingFee:
                    fees += entry.Amount;
                    break;

                case LedgerEntryTypes.Refund:
                    refunds += entry.Amount;
                    taxableRefunds += entry.TaxableValue;
                    break;

                // What comes back on a reversal reduces what the platform charged, so it is netted
                // against the charges rather than shown as income to the seller. A statement that
                // listed it separately would show a seller a credit they never received.
                case LedgerEntryTypes.RefundCommissionReversal:
                    commission -= entry.Amount;
                    break;

                case LedgerEntryTypes.Adjustment:
                    adjustments += entry.SignedAmount;
                    break;

                case LedgerEntryTypes.Payout:
                    payouts += entry.Amount;
                    break;

                default:
                    break;
            }
        }

        return new SettlementTotals(
            grossSales,
            taxableSales,
            Math.Max(0m, commission),
            fees,
            refunds,
            taxableRefunds,
            adjustments,
            payouts,
            movement);
    }

    /// <summary>Credits less debits, from a grouped sum.</summary>
    private static decimal Signed(IEnumerable<(LedgerDirection Direction, decimal Amount)> sums)
        => sums.Sum(sum => sum.Direction == LedgerDirection.Credit ? sum.Amount : -sum.Amount);

    /// <summary>Adds a new cycle to the context and hands it back.</summary>
    private SettlementCycle Track(SettlementCycle cycle)
    {
        context.Cycles.Add(cycle);
        return cycle;
    }

    [LoggerMessage(EventId = 1810, Level = LogLevel.Information,
        Message = "Closed the settlement period {PeriodStart:d MMM yyyy} to {PeriodEnd:d MMM yyyy} for "
                  + "vendor {VendorId}: {NetPayable} payable.")]
    private static partial void CycleClosed(
        ILogger logger,
        Guid vendorId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        decimal netPayable);
}
