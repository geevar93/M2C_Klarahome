using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Contracts.Payments;

/// <summary>Everything the payments module needs in order to send money back.</summary>
/// <remarks>
/// There is no payment id on it, and that is deliberate. The caller knows which <em>order</em> owes
/// money; which collection it came out of, whether it was a card or cash on delivery, and how much
/// of it is still refundable are all facts of the payments schema, and a caller that had to resolve
/// them first would be a caller reading that schema.
/// </remarks>
/// <param name="OrderId">The order the money was collected for.</param>
/// <param name="Amount">What to send back, inclusive of tax.</param>
/// <param name="Reason">Why. Required, and it goes on the audit trail.</param>
/// <param name="SubOrderId">The seller's part it relates to, when it relates to one.</param>
/// <param name="ReturnId">The RMA that caused it, when a return did.</param>
/// <param name="IdempotencyKey">
/// The caller's key, unique per tenant. A duplicate here is money out of the door twice, so the
/// unique index on it — not this method — is the real guarantee.
/// </param>
public sealed record RefundInitiationRequest(
    Guid OrderId,
    decimal Amount,
    string Reason,
    Guid? SubOrderId,
    Guid? ReturnId,
    string IdempotencyKey);

/// <summary>What became of a refund request.</summary>
/// <remarks>
/// <see cref="IsAwaitingApproval"/> is the field callers actually branch on. A refund above the
/// configured threshold is created and deliberately not sent — it waits in the approvals queue for a
/// second pair of eyes (docs/07-security-compliance.md §4) — and a caller that treated that as
/// failure would tell a shopper their refund had been refused when it had not.
/// </remarks>
/// <param name="RefundId">The refund.</param>
/// <param name="Status">Where it stands, in the payments module's vocabulary.</param>
/// <param name="Amount">What is going back.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="IsAwaitingApproval">Whether it is waiting for a second signature rather than moving.</param>
public sealed record RefundInitiationResult(
    Guid RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    bool IsAwaitingApproval);

/// <summary>
/// Sends money back to where it came from (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The mirror of <see cref="IPaymentInitiation"/>, and it points the same way round for the same
/// reason: Payments owns the conversation with the gateway and the maker–checker control over it,
/// and no other module may open a refund by writing to <c>payments.refunds</c>. The Returns module
/// decides <em>whether</em> a shopper is owed money and <em>how much</em>; it does not decide how it
/// travels, whether it needs a second signature, or which collection it comes out of.
/// </para>
/// <para>
/// Declared at Step 17 and implemented in the Payments module, which already raises refunds this way
/// for a cancellation. The Returns module registers no fallback: a deployment without a gateway
/// still refunds to store credit, which does not come through here at all.
/// </para>
/// <para>
/// Idempotent on <see cref="RefundInitiationRequest.IdempotencyKey"/>. Replaying a key returns the
/// original refund rather than raising a second one.
/// </para>
/// </remarks>
public interface IRefundInitiation
{
    /// <summary>Raises the refund, or explains why it could not be raised.</summary>
    /// <param name="request">What to send back, and why.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<RefundInitiationResult>> RefundAsync(
        RefundInitiationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What is still refundable against an order, or zero when nothing was ever collected.
    /// </summary>
    /// <remarks>
    /// Asked before a refund is offered so a shopper is never promised money that is not there: a
    /// cash-on-delivery parcel refused at the door was never paid for, and a second return against
    /// an order already refunded in full has nothing left to give.
    /// </remarks>
    /// <param name="orderId">The order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<decimal> RefundableAmountAsync(Guid orderId, CancellationToken cancellationToken = default);
}
