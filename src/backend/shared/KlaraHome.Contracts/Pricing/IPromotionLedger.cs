namespace KlaraHome.Contracts.Pricing;

/// <summary>One promotion to be redeemed against an order.</summary>
/// <param name="PromotionId">The promotion the quote applied.</param>
/// <param name="DiscountAmount">What it took off, as the quote computed it.</param>
public sealed record PromotionRedemptionRequest(Guid PromotionId, decimal DiscountAmount);

/// <summary>
/// Commits and reverses the promotions a quote applied (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IPriceQuoteEngine"/> on purpose. Pricing a basket happens on every
/// cart render and must change nothing; spending one of a hundred available coupons happens once,
/// when the order is placed. Folding the two together would burn a usage limit every time a
/// shopper refreshed their cart.
/// </para>
/// <para>
/// Both calls are idempotent, because the events that drive them are delivered at least once: the
/// redemption row is unique per <c>(promotion, order)</c>, and a reversal that finds nothing to
/// reverse reports zero rather than failing.
/// </para>
/// </remarks>
public interface IPromotionLedger
{
    /// <summary>
    /// Records that an order used these promotions, and increments their usage counters.
    /// </summary>
    /// <remarks>
    /// The increment is a conditional update against the limit, so a promotion with one use left
    /// and two orders racing for it grants one. A promotion whose limit is exhausted between
    /// quoting and placing is reported back rather than silently honoured.
    /// </remarks>
    /// <param name="orderId">The order.</param>
    /// <param name="customerId">The shopper, for the per-customer limit.</param>
    /// <param name="redemptions">What the quote applied.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The promotions that could not be redeemed, empty when every one was.</returns>
    ValueTask<IReadOnlyList<Guid>> RedeemAsync(
        Guid orderId,
        Guid? customerId,
        IReadOnlyList<PromotionRedemptionRequest> redemptions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverses every redemption made against one order, giving the uses back.
    /// </summary>
    /// <remarks>
    /// The rows are marked reversed rather than deleted: a per-customer limit that forgot a
    /// cancelled order would let one shopper cycle a single-use coupon indefinitely.
    /// </remarks>
    /// <param name="orderId">The order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many redemptions this call reversed.</returns>
    ValueTask<int> ReverseAsync(Guid orderId, CancellationToken cancellationToken = default);
}
