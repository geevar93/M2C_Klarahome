using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Events;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Payments.Infrastructure.Processing;

/// <summary>What one ingestion run imported and how much of it agreed.</summary>
/// <param name="Imported">Reports newly imported.</param>
/// <param name="Skipped">Reports already held, which a lookback window is expected to find.</param>
/// <param name="Entries">Lines read across the imported reports.</param>
/// <param name="Matched">Lines that agreed with a collection recorded here.</param>
/// <param name="Mismatched">Lines that did not, and were alerted rather than repaired.</param>
internal sealed record SettlementIngestionSummary(
    int Imported,
    int Skipped,
    int Entries,
    int Matched,
    int Mismatched);

/// <summary>
/// Pulls settlement reports and matches them against what this platform recorded
/// (docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// The third reconciliation question, and the only one that can catch a payment that was captured,
/// never disputed and never actually paid out. A webhook tells us a payment succeeded; a settlement
/// report tells us the money reached the bank, and the two are not the same claim.
/// </para>
/// <para>
/// Ingestion is idempotent on the gateway's settlement id, which is what lets the job use a lookback
/// window: re-reading the last three days catches a report that arrived late without importing
/// yesterday's twice.
/// </para>
/// <para>
/// <b>A mismatch is recorded and alerted; nothing is repaired.</b> A line whose amount does not agree
/// with our payment, or that points at a payment we do not have, becomes a
/// <c>Mismatched</c> entry a human can open and a <c>PaymentMismatchDetected</c> event that puts it
/// in front of them. Adjusting our figure to match the gateway's would be destroying the evidence.
/// </para>
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="providers">Finds the adapter to pull from.</param>
/// <param name="events">Raises the mismatch alerts.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what one run imported.</param>
internal sealed partial class SettlementIngestionService(
    PaymentsDbContext context,
    PaymentProviderRegistry providers,
    PaymentsEventPublisher events,
    IClock clock,
    ILogger<SettlementIngestionService> logger)
{
    /// <summary>Pulls the reports for a window, imports what is new, and matches their lines.</summary>
    /// <param name="from">Start of the window, inclusive.</param>
    /// <param name="to">End of the window, inclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<SettlementIngestionSummary>> IngestAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var resolved = providers.Require(providers.Default?.Name);

        if (resolved.IsFailure)
        {
            return Result.Failure<SettlementIngestionSummary>(resolved.Error);
        }

        var provider = resolved.Value;

        var pulled = await provider.FetchSettlementsAsync(from, to, cancellationToken).ConfigureAwait(false);

        if (pulled.IsFailure)
        {
            return Result.Failure<SettlementIngestionSummary>(pulled.Error);
        }

        var imported = 0;
        var skipped = 0;
        var entries = 0;
        var matched = 0;
        var mismatched = 0;

        foreach (var report in pulled.Value)
        {
            var held = await context.GatewaySettlements
                .AnyAsync(
                    settlement => settlement.Provider == provider.Name
                                  && settlement.ProviderSettlementId == report.ProviderSettlementId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (held)
            {
                skipped++;
                continue;
            }

            var settlement = GatewaySettlement.Import(
                provider.Name,
                report.ProviderSettlementId,
                report.Amount,
                report.Fees,
                report.Tax,
                report.CurrencyCode,
                report.Utr,
                report.Status,
                report.SettledAt,
                report.Raw,
                clock.UtcNow);

            context.GatewaySettlements.Add(settlement);

            foreach (var line in report.Entries)
            {
                var entry = GatewaySettlementEntry.Record(
                    settlement.Id,
                    line.EntryType,
                    line.ProviderEntryId,
                    line.ProviderPaymentId,
                    line.Amount,
                    line.Fee,
                    line.Tax,
                    line.Debit,
                    line.Credit,
                    line.OccurredAt);

                settlement.Add(entry);

                if (await MatchAsync(settlement, entry, cancellationToken).ConfigureAwait(false))
                {
                    matched++;
                }
                else
                {
                    mismatched++;
                }

                entries++;
            }

            settlement.RederiveCounts();
            imported++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var summary = new SettlementIngestionSummary(imported, skipped, entries, matched, mismatched);
        IngestionFinished(logger, summary.Imported, summary.Skipped, summary.Matched, summary.Mismatched);

        return Result.Success(summary);
    }

    /// <summary>
    /// Matches one settlement line to a collection, and says whether it agreed.
    /// </summary>
    /// <remarks>
    /// Only <c>payment</c> lines are matched against payments. A fee adjustment or a Route transfer
    /// is a real movement with no payment of ours behind it, and calling those unmatched would fill
    /// the mismatch report with noise nobody can act on — so they are marked matched, which here
    /// means "accounted for", and the amounts still roll up into the report's totals.
    /// </remarks>
    private async Task<bool> MatchAsync(
        GatewaySettlement settlement,
        GatewaySettlementEntry entry,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(entry.EntryType, "payment", StringComparison.OrdinalIgnoreCase))
        {
            entry.Match(Guid.Empty);
            return true;
        }

        var payment = string.IsNullOrWhiteSpace(entry.ProviderPaymentId)
            ? null
            : await context.Payments
                .FirstOrDefaultAsync(
                    candidate => candidate.ProviderPaymentId == entry.ProviderPaymentId,
                    cancellationToken)
                .ConfigureAwait(false);

        if (payment is null)
        {
            entry.Mismatch(null, "The gateway settled a payment this platform has no record of.");

            events.Mismatch(
                MismatchKinds.UnmatchedEntry,
                $"Settlement {settlement.ProviderSettlementId} paid out {entry.Amount:0.00} for gateway "
                + $"payment {entry.ProviderPaymentId}, which this platform did not open.",
                settlement.CurrencyCode,
                settlementId: settlement.Id,
                reference: entry.ProviderPaymentId,
                actual: entry.Amount);

            return false;
        }

        if (payment.AmountCaptured != entry.Amount)
        {
            entry.Mismatch(
                payment.Id,
                $"Settled {entry.Amount:0.00} against a capture of {payment.AmountCaptured:0.00}.");

            events.Mismatch(
                MismatchKinds.Amount,
                $"Settlement {settlement.ProviderSettlementId} paid out {entry.Amount:0.00} for order "
                + $"{payment.OrderNumber}, which was captured at {payment.AmountCaptured:0.00}.",
                settlement.CurrencyCode,
                payment.Id,
                payment.OrderId,
                settlement.Id,
                entry.ProviderPaymentId,
                payment.AmountCaptured,
                entry.Amount);

            return false;
        }

        entry.Match(payment.Id);
        payment.MarkSettled(settlement.Id);

        return true;
    }

    [LoggerMessage(EventId = 1560, Level = LogLevel.Information,
        Message = "Settlement ingestion imported {Imported} report(s), skipped {Skipped} already held; "
                  + "{Matched} line(s) agreed and {Mismatched} did not.")]
    private static partial void IngestionFinished(
        ILogger logger,
        int imported,
        int skipped,
        int matched,
        int mismatched);
}
