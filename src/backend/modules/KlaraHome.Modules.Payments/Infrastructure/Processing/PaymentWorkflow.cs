using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Payments.Application;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Events;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Payments.Infrastructure.Processing;

/// <summary>
/// The single place a fact about money is applied.
/// </summary>
/// <remarks>
/// <para>
/// Four routes learn things about a payment — the browser's callback, a webhook, the reconciliation
/// sweep, and an operator pressing <em>sync</em> — and all four reduce to a
/// <see cref="ProviderPayment"/> and come through here. If that logic lived in the handlers, the
/// webhook's idea of "captured" and the reconciliation job's would drift, and the day they did the
/// platform would have two answers to whether it holds a customer's money.
/// </para>
/// <para>
/// The order of operations is deliberate and is the most important thing in this file. The order is
/// confirmed <b>first</b>, through <see cref="IOrderPaymentSync"/>, which commits in its own
/// transaction; only then is the payment row moved, and the caller commits that. Doing it the other
/// way round would let the payment say <em>captured</em> while the order stayed unconfirmed, with
/// the gateway event already marked processed and nothing left to retry it. This way round, a
/// failure between the two leaves the event pending, and the retry re-applies a confirmation that
/// is idempotent.
/// </para>
/// <para>
/// It saves nothing. Every method mutates the tracked graph and enqueues the outbox rows, and the
/// caller commits — which is what keeps a payment and its announcement in one transaction (ADR-003).
/// </para>
/// </remarks>
/// <param name="orders">The seam into ordering. Confirms, fails and mirrors refunds.</param>
/// <param name="events">Announces what happened.</param>
/// <param name="settings">Supplies the refund governance levers.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was applied, and what did not add up.</param>
internal sealed partial class PaymentWorkflow(
    IOrderPaymentSync orders,
    PaymentsEventPublisher events,
    IStoreSettings settings,
    IClock clock,
    ILogger<PaymentWorkflow> logger)
{
    /// <summary>
    /// Applies what the gateway says about a payment, whichever route heard it.
    /// </summary>
    /// <remarks>
    /// The attempt is recorded first and unconditionally, before any decision about whether the fact
    /// is actionable. A try that this platform then refuses to act on — a stale failure after a
    /// capture, a short capture that does not match the order — is exactly the try somebody will
    /// need to see when they ask what happened.
    /// </remarks>
    /// <param name="payment">The collection, with its attempts loaded.</param>
    /// <param name="fact">What the gateway currently says, re-fetched from its API.</param>
    /// <param name="source">Which route heard it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> ApplyAsync(
        Payment payment,
        ProviderPayment fact,
        PaymentAttemptSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(fact);

        var now = clock.UtcNow;

        payment.Record(PaymentAttempt.Record(
            payment.Id,
            fact.ProviderPaymentId,
            fact.Status,
            fact.Method,
            fact.Amount,
            fact.Detail,
            fact.ErrorCode,
            fact.ErrorDescription,
            source,
            fact.OccurredAt ?? now));

        if (fact.IsCaptured)
        {
            return await ApplyCaptureAsync(payment, fact, now, cancellationToken).ConfigureAwait(false);
        }

        if (fact.IsFailed)
        {
            return await ApplyFailureAsync(payment, fact, now, cancellationToken).ConfigureAwait(false);
        }

        if (fact.IsAuthorized)
        {
            // Authorised and not captured. The order stays awaiting payment on purpose: money that
            // is merely held is money nobody has, and confirming on it would promise a seller stock
            // against a hold that can still lapse.
            payment.Authorize(fact.ProviderPaymentId, fact.Method, fact.OccurredAt ?? now);
        }

        payment.MarkReconciled(now);
        return Result.Success();
    }

    /// <summary>
    /// Applies a capture: verifies the amount, confirms the order, then moves the payment.
    /// </summary>
    /// <remarks>
    /// The amount check is the control docs/07-security-compliance.md §4 requires, and it is a
    /// refusal rather than a warning. A gateway that took less than the order asked for is a
    /// discrepancy for a human — confirming anyway would ship goods against money that is not there,
    /// and the mismatch event is what puts it in front of somebody.
    /// </remarks>
    private async Task<Result> ApplyCaptureAsync(
        Payment payment,
        ProviderPayment fact,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (fact.Amount < payment.Amount)
        {
            events.Mismatch(
                MismatchKinds.Amount,
                $"The gateway captured {fact.Amount:0.00} against order {payment.OrderNumber}, "
                + $"which is for {payment.Amount:0.00}.",
                payment.CurrencyCode,
                payment.Id,
                payment.OrderId,
                reference: fact.ProviderPaymentId,
                expected: payment.Amount,
                actual: fact.Amount);

            ShortCapture(logger, payment.OrderNumber, fact.Amount, payment.Amount);

            return Result.Failure(PaymentsErrors.AmountMismatch(payment.Amount, fact.Amount));
        }

        var alreadySettled = PaymentLifecycle.IsSettled(payment.Status);

        // Ordering matters: the order is confirmed in its own transaction first, and the payment row
        // moves only once that has succeeded. A failure here leaves the caller's event pending, and
        // the retry re-applies a confirmation that is idempotent by contract.
        var confirmed = await orders
            .MarkPaidAsync(
                new PaymentCaptureFact(
                    payment.OrderId,
                    payment.Id,
                    fact.Method.ToString(),
                    fact.ProviderPaymentId,
                    fact.Amount,
                    fact.OccurredAt ?? now),
                cancellationToken)
            .ConfigureAwait(false);

        if (confirmed.IsFailure)
        {
            OrderNotConfirmed(logger, payment.OrderNumber, confirmed.Error.Code, confirmed.Error.Message);
            return confirmed;
        }

        payment.Capture(fact.ProviderPaymentId, fact.Method, fact.Amount, fact.OccurredAt ?? now);
        payment.MarkReconciled(now);

        // Announced only on the transition, not on every redelivery of the same capture. A shopper
        // who is emailed their receipt three times is a shopper who rings up to ask why.
        if (!alreadySettled)
        {
            events.Captured(payment);
        }

        return Result.Success();
    }

    private async Task<Result> ApplyFailureAsync(
        Payment payment,
        ProviderPayment fact,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!payment.Fail(fact.ErrorCode, fact.ErrorDescription, fact.OccurredAt ?? now))
        {
            // Already failed, or already settled and the failure is a stale event about an earlier
            // attempt. The attempt row above still records that it arrived.
            payment.MarkReconciled(now);
            return Result.Success();
        }

        var recorded = await orders
            .MarkPaymentFailedAsync(
                new PaymentFailureFact(
                    payment.OrderId,
                    payment.Id,
                    fact.ErrorCode,
                    fact.ErrorDescription),
                cancellationToken)
            .ConfigureAwait(false);

        if (recorded.IsFailure)
        {
            return recorded;
        }

        payment.MarkReconciled(now);
        events.Failed(payment);

        return Result.Success();
    }

    /// <summary>
    /// Applies what the gateway says about a refund.
    /// </summary>
    /// <remarks>
    /// The payment's refunded total is re-derived from the refunds rather than incremented, so a
    /// redelivered <c>refund.processed</c> cannot refund the shopper twice on paper. The mirror on
    /// the order is updated afterwards, and only when the total actually moved.
    /// </remarks>
    /// <param name="payment">The collection, with its refunds loaded.</param>
    /// <param name="refund">The refund the gateway is talking about.</param>
    /// <param name="fact">What it says.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> ApplyRefundAsync(
        Payment payment,
        Refund refund,
        ProviderRefund fact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(refund);
        ArgumentNullException.ThrowIfNull(fact);

        var now = clock.UtcNow;
        var moved = false;

        if (fact.IsProcessed)
        {
            moved = refund.MarkProcessed(fact.ProviderRefundId, fact.OccurredAt ?? now);
        }
        else if (fact.IsFailed)
        {
            refund.MarkFailed(fact.Error);
        }
        else
        {
            refund.MarkProcessing(fact.ProviderRefundId);
        }

        payment.RederiveRefunds();

        if (!moved)
        {
            return Result.Success();
        }

        var mirrored = await orders
            .RecordRefundAsync(
                new PaymentRefundFact(payment.OrderId, payment.AmountRefunded, payment.AmountCaptured),
                cancellationToken)
            .ConfigureAwait(false);

        if (mirrored.IsFailure)
        {
            return mirrored;
        }

        events.Refunded(payment, refund);
        return Result.Success();
    }

    /// <summary>
    /// Raises a refund against a collection, applying the maker-checker threshold.
    /// </summary>
    /// <remarks>
    /// The threshold is read from the store's own settings rather than from configuration, because
    /// it is a governance decision the business revisits — usually after an incident — and it must
    /// not need a deployment (docs/07-security-compliance.md §4).
    /// </remarks>
    /// <param name="payment">The collection, with its refunds loaded.</param>
    /// <param name="amount">What to send back.</param>
    /// <param name="reason">Why.</param>
    /// <param name="idempotencyKey">The key, unique per tenant.</param>
    /// <param name="initiatedBy">Who raised it, or null when the platform did.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<Refund>> RaiseRefundAsync(
        Payment payment,
        decimal amount,
        string reason,
        string idempotencyKey,
        Guid? initiatedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);

        if (payment.AmountCaptured <= 0m)
        {
            return Result.Failure<Refund>(PaymentsErrors.NothingToRefund);
        }

        if (amount <= 0m || amount > payment.AmountRefundable)
        {
            return Result.Failure<Refund>(PaymentsErrors.RefundExceedsCaptured(payment.AmountRefundable));
        }

        var governance = await settings.GetAsync<PaymentSettings>(cancellationToken).ConfigureAwait(false);

        var refund = Refund.Raise(
            payment.Id,
            payment.OrderId,
            amount,
            payment.CurrencyCode,
            reason,
            idempotencyKey,
            initiatedBy,
            governance.RefundApprovalThreshold,
            clock.UtcNow);

        if (governance.PreferInstantRefunds)
        {
            refund.SetSpeed(RefundSpeed.Optimum);
        }

        payment.Add(refund);

        return Result.Success(refund);
    }

    [LoggerMessage(EventId = 1530, Level = LogLevel.Error,
        Message = "Order {OrderNumber} was not confirmed after capture: {Code} {Detail}")]
    private static partial void OrderNotConfirmed(
        ILogger logger,
        string orderNumber,
        string code,
        string detail);

    [LoggerMessage(EventId = 1531, Level = LogLevel.Error,
        Message = "The gateway captured {Captured} against order {OrderNumber}, which is for {Expected}. "
                  + "The order was not confirmed.")]
    private static partial void ShortCapture(
        ILogger logger,
        string orderNumber,
        decimal captured,
        decimal expected);
}
