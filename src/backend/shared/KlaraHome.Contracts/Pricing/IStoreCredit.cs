namespace KlaraHome.Contracts.Pricing;

/// <summary>Why store credit moved. Recorded on the transaction and shown to the customer.</summary>
public static class StoreCreditReasons
{
    /// <summary>Spent against an order.</summary>
    public const string OrderPayment = "order-payment";

    /// <summary>Returned because the order it was spent on was cancelled.</summary>
    public const string OrderCancelled = "order-cancelled";

    /// <summary>A refund the business chose to pay as credit rather than to the card.</summary>
    public const string Refund = "refund";

    /// <summary>Loyalty accrued on a completed order.</summary>
    public const string LoyaltyAccrual = "loyalty-accrual";

    /// <summary>A goodwill or correction entry made by staff.</summary>
    public const string Adjustment = "adjustment";
}

/// <summary>A customer's store-credit balance.</summary>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Balance">What they have. Never negative.</param>
/// <param name="CurrencyCode">ISO 4217 code the balance is in.</param>
/// <param name="IsActive">Whether the wallet may still be spent from.</param>
public sealed record StoreCreditBalance(Guid CustomerId, decimal Balance, string CurrencyCode, bool IsActive);

/// <summary>
/// Reads and moves store credit from outside the Pricing module (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// Orders spends it, Payments and Returns give it back, and Settlements never sees it — store
/// credit is the platform's own liability to a customer, not a seller's. None of them may write to
/// <c>pricing.wallets</c>, so this contract is the whole of their access.
/// </para>
/// <para>
/// Every mover is keyed on a caller-supplied reference, and a second call with the same reference
/// is a no-op that returns the balance it already produced. That is not a nicety: the events that
/// drive a refund are delivered at least once, and a wallet that credited twice would be money
/// given away.
/// </para>
/// <para>
/// The whole surface is behind the <c>pricing.store-credit</c> feature flag. With the flag off the
/// balance reads as zero and every mover refuses, so a deployment that has not enabled loyalty
/// cannot accidentally accrue it.
/// </para>
/// </remarks>
public interface IStoreCredit
{
    /// <summary>What a customer has. A customer with no wallet reads as a zero balance.</summary>
    /// <param name="customerId">The shopper.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<StoreCreditBalance> GetBalanceAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Spends credit. Returns the amount actually taken, which is zero when the balance was short.
    /// </summary>
    /// <remarks>
    /// All or nothing on the requested amount: a partial debit would leave the order underpaid by
    /// a figure nobody quoted. The caller decides what to do with a zero.
    /// </remarks>
    /// <param name="customerId">The shopper.</param>
    /// <param name="amount">How much to spend. Must be positive.</param>
    /// <param name="reason">One of <see cref="StoreCreditReasons"/>.</param>
    /// <param name="referenceType">What is spending it — <c>order</c>.</param>
    /// <param name="referenceId">The order, for idempotency.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<decimal> RedeemAsync(
        Guid customerId,
        decimal amount,
        string reason,
        string referenceType,
        Guid referenceId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds credit — a refund, a loyalty accrual, or a goodwill adjustment.</summary>
    /// <param name="customerId">The shopper.</param>
    /// <param name="amount">How much to add. Must be positive.</param>
    /// <param name="reason">One of <see cref="StoreCreditReasons"/>.</param>
    /// <param name="referenceType">What caused it — <c>order</c>, <c>return</c>, <c>staff</c>.</param>
    /// <param name="referenceId">The causing record, for idempotency.</param>
    /// <param name="expiresAt">When the credit lapses, or null for credit that does not.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<StoreCreditBalance> CreditAsync(
        Guid customerId,
        decimal amount,
        string reason,
        string referenceType,
        Guid referenceId,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default);
}
