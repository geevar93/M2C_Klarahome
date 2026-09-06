using KlaraHome.Contracts.Orders;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Contracts.Payments;

/// <summary>Everything a gateway needs to be asked for money against an order.</summary>
/// <param name="OrderId">The order being paid for.</param>
/// <param name="OrderNumber">Its human-readable number, which is what appears on the gateway's dashboard.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Amount">
/// What to collect: the order total less any store credit already redeemed. Never recomputed by the
/// gateway side — this is the figure the shopper agreed to on the review screen.
/// </param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="CustomerName">Prefilled on the checkout widget, so the shopper does not retype it.</param>
/// <param name="CustomerEmail">Prefilled, and where the gateway sends its own receipt.</param>
/// <param name="CustomerMobile">Prefilled in E.164, which is what a UPI collect request needs.</param>
/// <param name="IdempotencyKey">
/// The key the placement was made under. Passed through so a retried placement asks the gateway for
/// the same collection rather than opening a second one against one order.
/// </param>
public sealed record PaymentInitiationRequest(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    decimal Amount,
    string CurrencyCode,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerMobile,
    string IdempotencyKey);

/// <summary>
/// Opens a collection against a placed order (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The seam between ordering and payment, and it points this way round for the same reason the
/// checkout-to-ordering seam does. Orders owns the record of what was agreed and is the only thing
/// that knows an order exists to be paid for; Payments owns the conversation with the gateway. An
/// inversion — Payments reading <c>orders.orders</c> — would put a join across a schema boundary on
/// the one path where money changes hands.
/// </para>
/// <para>
/// Declared at Step 14 and implemented at Step 15. Until then the Orders module registers an
/// implementation that refuses politely, so a prepaid placement against a half-built platform gets a
/// 503 that names the reason rather than an order nobody can pay for. Cash on delivery does not go
/// through here at all and is unaffected.
/// </para>
/// <para>
/// The caller has already made the request idempotent, so this is invoked at most once per order per
/// placement key.
/// </para>
/// </remarks>
public interface IPaymentInitiation
{
    /// <summary>Opens the collection, or explains why it could not be opened.</summary>
    /// <param name="request">The order to collect for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<PaymentInstruction>> InitiateAsync(
        PaymentInitiationRequest request,
        CancellationToken cancellationToken = default);
}
