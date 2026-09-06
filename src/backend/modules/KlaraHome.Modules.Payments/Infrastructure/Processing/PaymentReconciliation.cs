using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Events;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Payments.Infrastructure.Processing;

/// <summary>What one reconciliation sweep found.</summary>
/// <param name="Examined">How many collections were asked about.</param>
/// <param name="Recovered">How many turned out to have been paid without us being told.</param>
/// <param name="Failed">How many the gateway confirms were refused.</param>
/// <param name="Mismatched">How many did not add up, and were alerted rather than repaired.</param>
internal sealed record ReconciliationSummary(int Examined, int Recovered, int Failed, int Mismatched);

/// <summary>
/// Asks the gateway about collections nobody has told us anything about
/// (docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// The recovery path for a lost webhook, and the reason an order cannot get stuck unpaid because a
/// single HTTP request went missing. Every collection that has been open longer than the grace
/// period is re-read from the gateway's API, and whatever the gateway says is applied through the
/// same workflow a webhook goes through — so a recovered capture confirms the order in exactly the
/// same way, and leaves an attempt row whose source says <c>Reconciliation</c>, which is how a run
/// of lost webhooks becomes visible.
/// </para>
/// <para>
/// It also checks the second of the three assertions in docs/08-integrations.md §1: that the
/// refunded total cached on a payment equals the sum of its completed refunds.
/// <b>A discrepancy is alerted and never repaired.</b> Silently rewriting our figure to match the
/// gateway's would destroy the only evidence that the two ever disagreed, which is precisely what a
/// reconciliation exists to preserve.
/// </para>
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="providers">Finds the adapter to ask.</param>
/// <param name="workflow">Applies whatever the gateway says.</param>
/// <param name="events">Raises the mismatch alerts.</param>
/// <param name="options">Supplies the grace period and the batch size.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what one sweep found.</param>
internal sealed partial class PaymentReconciliationService(
    PaymentsDbContext context,
    PaymentProviderRegistry providers,
    PaymentWorkflow workflow,
    PaymentsEventPublisher events,
    IOptionsMonitor<PaymentsOptions> options,
    IClock clock,
    ILogger<PaymentReconciliationService> logger)
{
    /// <summary>Runs one sweep and commits what it found.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<ReconciliationSummary>> SweepAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var cutoff = clock.UtcNow.AddMinutes(-settings.ReconciliationGraceMinutes);

        var due = await context.Payments
            .Include(payment => payment.Refunds)
            .Where(payment => payment.Provider != PaymentProviders.InternalCod
                              && (payment.Status == PaymentStatus.Created
                                  || payment.Status == PaymentStatus.Authorized)
                              && payment.OpenedAt <= cutoff)
            .OrderBy(payment => payment.OpenedAt)
            .Take(settings.ReconciliationBatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var recovered = 0;
        var failed = 0;
        var mismatched = 0;

        foreach (var payment in due)
        {
            var applied = await ExamineAsync(payment, cancellationToken).ConfigureAwait(false);

            switch (applied)
            {
                case ExamineOutcome.Recovered:
                    recovered++;
                    break;
                case ExamineOutcome.Failed:
                    failed++;
                    break;
                case ExamineOutcome.Mismatched:
                    mismatched++;
                    break;
                case ExamineOutcome.Unchanged:
                default:
                    break;
            }
        }

        mismatched += await AssertRefundTotalsAsync(cancellationToken).ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var summary = new ReconciliationSummary(due.Count, recovered, failed, mismatched);
        SweepFinished(logger, summary.Examined, summary.Recovered, summary.Failed, summary.Mismatched);

        return Result.Success(summary);
    }

    private async Task<ExamineOutcome> ExamineAsync(Payment payment, CancellationToken cancellationToken)
    {
        var resolved = providers.Require(payment.Provider);

        if (resolved.IsFailure || string.IsNullOrWhiteSpace(payment.ProviderOrderId))
        {
            return ExamineOutcome.Unchanged;
        }

        var listed = await resolved.Value
            .FetchPaymentsForOrderAsync(payment.ProviderOrderId, cancellationToken)
            .ConfigureAwait(false);

        if (listed.IsFailure)
        {
            return ExamineOutcome.Unchanged;
        }

        // A captured payment first, then a failed one. A collection with neither is one the shopper
        // simply never completed: the unpaid-order sweeper in Orders will time it out, and this sweep
        // deliberately does not fail it early on the gateway's silence.
        var fact = listed.Value.FirstOrDefault(candidate => candidate.IsCaptured)
                   ?? listed.Value.FirstOrDefault(candidate => candidate.IsFailed);

        if (fact is null)
        {
            payment.MarkReconciled(clock.UtcNow);
            return ExamineOutcome.Unchanged;
        }

        var applied = await workflow
            .ApplyAsync(payment, fact, PaymentAttemptSource.Reconciliation, cancellationToken)
            .ConfigureAwait(false);

        if (applied.IsFailure)
        {
            // The workflow already raised the mismatch event for a short capture; anything else is a
            // transient failure this sweep will meet again in fifteen minutes.
            return applied.Error.Code == "PAYMENT_AMOUNT_MISMATCH"
                ? ExamineOutcome.Mismatched
                : ExamineOutcome.Unchanged;
        }

        if (fact.IsCaptured)
        {
            RecoveredPayment(logger, payment.OrderNumber, fact.ProviderPaymentId);
            return ExamineOutcome.Recovered;
        }

        return fact.IsFailed ? ExamineOutcome.Failed : ExamineOutcome.Unchanged;
    }

    /// <summary>
    /// Asserts that each payment's refunded cache equals the sum of its completed refunds.
    /// </summary>
    /// <remarks>
    /// The cache exists so a payment list can show what has gone back without a join; this is the
    /// check that keeps it honest. It alerts and does not repair, for the same reason the nightly
    /// stock reconciliation does not: a cache that has drifted is evidence of a write that bypassed
    /// the domain, and fixing the number would hide the bug that produced it.
    /// </remarks>
    private async Task<int> AssertRefundTotalsAsync(CancellationToken cancellationToken)
    {
        var suspect = await context.Payments
            .Include(payment => payment.Refunds)
            .Where(payment => payment.AmountRefunded > 0m)
            .Where(payment => payment.AmountRefunded != payment.Refunds
                .Where(refund => refund.Status == RefundStatus.Processed)
                .Sum(refund => refund.Amount))
            .Take(options.CurrentValue.ReconciliationBatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var payment in suspect)
        {
            var summed = payment.Refunds
                .Where(refund => refund.Status == RefundStatus.Processed)
                .Sum(refund => refund.Amount);

            events.Mismatch(
                MismatchKinds.RefundTotal,
                $"Payment for order {payment.OrderNumber} records {payment.AmountRefunded:0.00} refunded, "
                + $"but its completed refunds sum to {summed:0.00}.",
                payment.CurrencyCode,
                payment.Id,
                payment.OrderId,
                expected: payment.AmountRefunded,
                actual: summed);
        }

        return suspect.Count;
    }

    private enum ExamineOutcome
    {
        Unchanged,
        Recovered,
        Failed,
        Mismatched,
    }

    [LoggerMessage(EventId = 1550, Level = LogLevel.Information,
        Message = "Reconciliation examined {Examined} collection(s): {Recovered} recovered, {Failed} failed, "
                  + "{Mismatched} mismatched.")]
    private static partial void SweepFinished(
        ILogger logger,
        int examined,
        int recovered,
        int failed,
        int mismatched);

    [LoggerMessage(EventId = 1551, Level = LogLevel.Warning,
        Message = "Recovered payment {Reference} for order {OrderNumber} from the gateway. "
                  + "No webhook was received for it.")]
    private static partial void RecoveredPayment(ILogger logger, string orderNumber, string reference);
}
