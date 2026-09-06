using KlaraHome.Contracts.Payments;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Payments.Infrastructure.Events;

/// <summary>
/// Announces what happened to the money (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// The outbox is resolved <b>keyed by this module's context</b>, and that is not decoration. The
/// unkeyed registration is first-wins and belongs to whichever module registered first; enqueuing
/// through it here would add the row to a different context's change tracker, this module's
/// <c>SaveChangesAsync</c> would not write it, and the event would be lost with no error anywhere.
/// </para>
/// <para>
/// Nothing is saved here. The event becomes real when the caller's transaction commits, and not
/// before (ADR-003) — so no shopper is ever told their payment succeeded by a transaction that then
/// rolled back.
/// </para>
/// <para>
/// Every event here is published <em>after</em> the order has already been confirmed or failed
/// through <c>IOrderPaymentSync</c>, never instead of it. These are for the consumers that only want
/// to know it happened; the confirmation itself is synchronous, because stock and invoices depend on
/// whether it succeeded.
/// </para>
/// </remarks>
/// <param name="outbox">This module's outbox, keyed by its context.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class PaymentsEventPublisher(
    [FromKeyedServices(typeof(PaymentsDbContext))] IOutbox outbox,
    IClock clock)
{
    /// <summary>Money was collected.</summary>
    /// <param name="payment">The collection, already captured.</param>
    public void Captured(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        outbox.Enqueue(new PaymentCaptured(
            payment.Id,
            payment.OrderId,
            payment.OrderNumber,
            payment.CustomerId,
            payment.Provider,
            payment.Method.ToString(),
            payment.ProviderPaymentId,
            payment.AmountCaptured,
            payment.CurrencyCode,
            payment.CapturedAt ?? clock.UtcNow));
    }

    /// <summary>A collection was refused, or never completed.</summary>
    /// <param name="payment">The collection.</param>
    public void Failed(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        outbox.Enqueue(new PaymentFailed(
            payment.Id,
            payment.OrderId,
            payment.OrderNumber,
            payment.CustomerId,
            payment.FailureCode,
            payment.FailureReason,
            payment.FailedAt ?? clock.UtcNow));
    }

    /// <summary>Money went back.</summary>
    /// <param name="payment">The collection it came out of.</param>
    /// <param name="refund">The refund, already processed.</param>
    public void Refunded(Payment payment, Refund refund)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(refund);

        outbox.Enqueue(new RefundProcessed(
            refund.Id,
            payment.Id,
            payment.OrderId,
            payment.OrderNumber,
            refund.SubOrderId,
            refund.ReturnId,
            payment.CustomerId,
            refund.Amount,
            refund.CurrencyCode,
            refund.ProviderRefundId,
            refund.CompletedAt ?? clock.UtcNow));
    }

    /// <summary>Cash was taken at a door, or remitted by the courier.</summary>
    /// <param name="collection">The cash record.</param>
    public void CashRecorded(CodCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var remitted = collection.Status == CodCollectionStatus.Remitted;

        outbox.Enqueue(new CodCashRecorded(
            collection.Id,
            collection.OrderId,
            collection.SubOrderId,
            collection.VendorId,
            (remitted ? collection.RemittedAmount : collection.CollectedAmount) ?? collection.Amount,
            collection.CurrencyCode,
            remitted,
            (remitted ? collection.RemittedAt : collection.CollectedAt) ?? clock.UtcNow));
    }

    /// <summary>The gateway's books and ours do not agree.</summary>
    /// <param name="kind">What disagreed.</param>
    /// <param name="detail">A sentence an operator can act on.</param>
    /// <param name="currencyCode">ISO 4217 code the amounts are in.</param>
    /// <param name="paymentId">The collection concerned, when one was resolved.</param>
    /// <param name="orderId">The order concerned, when one was resolved.</param>
    /// <param name="settlementId">The settlement it was found in, when it was.</param>
    /// <param name="reference">The gateway identifier the mismatch is about.</param>
    /// <param name="expected">What this platform recorded.</param>
    /// <param name="actual">What the gateway reports.</param>
    public void Mismatch(
        string kind,
        string detail,
        string currencyCode,
        Guid? paymentId = null,
        Guid? orderId = null,
        Guid? settlementId = null,
        string? reference = null,
        decimal? expected = null,
        decimal? actual = null)
        => outbox.Enqueue(new PaymentMismatchDetected(
            kind,
            paymentId,
            orderId,
            settlementId,
            reference,
            expected,
            actual,
            currencyCode,
            detail,
            clock.UtcNow));
}

/// <summary>The mismatch kinds this module reports, so a consumer can switch on them.</summary>
internal static class MismatchKinds
{
    /// <summary>This platform holds a captured payment the gateway has no record of.</summary>
    public const string MissingAtGateway = "missing-at-gateway";

    /// <summary>The gateway captured a different amount from the one the order asked for.</summary>
    public const string Amount = "amount";

    /// <summary>A settlement line points at a payment this platform does not have.</summary>
    public const string UnmatchedEntry = "unmatched-entry";

    /// <summary>The refunded total on a payment does not equal the sum of its completed refunds.</summary>
    public const string RefundTotal = "refund-total";
}
