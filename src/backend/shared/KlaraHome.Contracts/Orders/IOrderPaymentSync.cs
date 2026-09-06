using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Contracts.Orders;

/// <summary>What an order looks like to the module collecting money for it.</summary>
/// <remarks>
/// The few facts a gateway conversation needs, and deliberately not the order. Payments never learns
/// what was bought, from whom, or where it is going — a collection is an amount, a currency and a
/// reference, and widening this record would make the payments schema a second copy of the sale.
/// </remarks>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its human-readable number, which is the gateway receipt.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="AmountPayable">The grand total less store credit: what a gateway is asked for.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="PaymentMethod">Prepaid or cash on delivery, as the order was placed.</param>
/// <param name="IsAwaitingPayment">Whether any part of it is still waiting for money.</param>
/// <param name="IsPaid">Whether the order's mirrored payment status already says paid.</param>
/// <param name="PlacedAt">When it was placed. The retry window is measured from here.</param>
/// <param name="CustomerName">Prefilled on the checkout widget.</param>
/// <param name="CustomerEmail">Prefilled, and where the gateway sends its own receipt.</param>
/// <param name="CustomerMobile">Prefilled in E.164, which is what a UPI collect request needs.</param>
public sealed record OrderPaymentView(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    decimal AmountPayable,
    string CurrencyCode,
    string PaymentMethod,
    bool IsAwaitingPayment,
    bool IsPaid,
    DateTimeOffset PlacedAt,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerMobile);

/// <summary>What the gateway said, in the words the order machine needs.</summary>
/// <param name="OrderId">The order the money was collected for.</param>
/// <param name="PaymentId">The collection, so the timeline can name it.</param>
/// <param name="Method">The rail it was actually paid on, as the gateway reports it.</param>
/// <param name="Reference">The gateway's payment id, which is what a support call quotes.</param>
/// <param name="AmountCaptured">What was actually taken, verified against the gateway's API.</param>
/// <param name="CapturedAt">When the gateway captured it.</param>
public sealed record PaymentCaptureFact(
    Guid OrderId,
    Guid PaymentId,
    string Method,
    string Reference,
    decimal AmountCaptured,
    DateTimeOffset CapturedAt);

/// <summary>Why a collection did not happen.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="PaymentId">The collection that failed.</param>
/// <param name="FailureCode">The gateway's own code, kept verbatim so it can be looked up.</param>
/// <param name="Reason">What the shopper can be told.</param>
public sealed record PaymentFailureFact(
    Guid OrderId,
    Guid PaymentId,
    string? FailureCode,
    string? Reason);

/// <summary>Money that has gone back, in total, for one order.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="AmountRefunded">Everything refunded against it so far, not just this refund.</param>
/// <param name="AmountCaptured">Everything captured against it, so the mirror can be derived.</param>
public sealed record PaymentRefundFact(
    Guid OrderId,
    decimal AmountRefunded,
    decimal AmountCaptured);

/// <summary>
/// The seam Payments reaches ordering through (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The return leg of <see cref="Payments.IPaymentInitiation"/>, and it points this way round for the
/// same reason: Orders owns the record of the sale and the state machine that moves it, Payments
/// owns the conversation with the gateway, and neither reads the other's schema. The architecture
/// diagram's <c>PAY-&gt;&gt;O: MarkPaid → Confirmed</c> is this interface.
/// </para>
/// <para>
/// Synchronous rather than an integration event, deliberately. Confirming an order commits stock and
/// raises the events every downstream module runs on, and the webhook handler needs to know whether
/// that succeeded before it marks the gateway event processed — an event fired into the outbox would
/// leave the payment recorded and the order unconfirmed with nothing holding the two together.
/// </para>
/// <para>
/// Every method is idempotent. A webhook is delivered at least once, a reconciliation sweep may
/// arrive at the same fact independently, and both must be safe to apply twice: an order already
/// confirmed for this payment is a success, not a conflict.
/// </para>
/// </remarks>
public interface IOrderPaymentSync
{
    /// <summary>Reads the few facts a collection needs, or fails if the order is not visible.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="customerId">The shopper it must belong to, or null to read as the platform.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<OrderPaymentView>> GetAsync(
        Guid orderId,
        Guid? customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Confirms every part of the order that was waiting for this money.</summary>
    /// <param name="capture">What the gateway captured, already verified against its API.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> MarkPaidAsync(PaymentCaptureFact capture, CancellationToken cancellationToken = default);

    /// <summary>Records that the collection failed, leaving the order retryable.</summary>
    /// <param name="failure">What the gateway refused, and why.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> MarkPaymentFailedAsync(
        PaymentFailureFact failure,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the order's mirrored payment status after money went back.</summary>
    /// <param name="refund">The running totals, from which the mirror is derived.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> RecordRefundAsync(PaymentRefundFact refund, CancellationToken cancellationToken = default);
}
