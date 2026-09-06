namespace KlaraHome.Contracts.Inventory;

/// <summary>
/// Everything another module is allowed to know about the stock behind one offer.
/// </summary>
/// <remarks>
/// Aggregated across every warehouse holding the listing, because that is the question a cart, a
/// product page and a checkout all actually have. Which warehouse the units come out of is an
/// allocation decision, and it belongs to Inventory and Shipping rather than to the caller.
/// </remarks>
/// <param name="ListingId">The offer.</param>
/// <param name="QuantityOnHand">Units physically held.</param>
/// <param name="QuantityReserved">Units already held for somebody else's cart or order.</param>
/// <param name="QuantityAvailable">
/// What may still be sold: on hand less reserved, floored at zero. This is the number a caller
/// compares a requested quantity against, and the only one it should.
/// </param>
/// <param name="AllowsBackorder">Whether the seller accepts orders beyond what is on hand.</param>
/// <param name="AllowsPreorder">Whether the offer may be sold before it is released.</param>
/// <param name="PreorderAvailableAt">When a pre-ordered unit is expected to ship.</param>
/// <param name="IsTracked">
/// Whether any stock item exists for the offer at all. False means Inventory has never heard of
/// it, which is not the same as zero — an untracked offer is one nobody has stocked yet.
/// </param>
public sealed record StockAvailability(
    Guid ListingId,
    int QuantityOnHand,
    int QuantityReserved,
    int QuantityAvailable,
    bool AllowsBackorder,
    bool AllowsPreorder,
    DateTimeOffset? PreorderAvailableAt,
    bool IsTracked)
{
    /// <summary>Whether <paramref name="quantity"/> units can be sold right now.</summary>
    /// <remarks>
    /// Backorder and pre-order both say yes regardless of the count, which is what those flags
    /// mean. An untracked offer says no: an offer nobody has stocked is not one to sell.
    /// </remarks>
    /// <param name="quantity">How many units the caller wants.</param>
    public bool CanFulfil(int quantity)
        => IsTracked && (AllowsBackorder || AllowsPreorder || QuantityAvailable >= quantity);
}

/// <summary>How a reservation is settled once its owner has decided.</summary>
public enum ReservationOutcome
{
    /// <summary>The sale happened. The held units leave stock for good.</summary>
    Committed = 0,

    /// <summary>The sale did not happen. The held units go back on sale.</summary>
    Released = 1,
}

/// <summary>
/// Reads stock and holds it, from outside the Inventory module (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// A cart shows availability, a checkout holds it, and an order commits or releases it. None of
/// those may join to <c>inventory.stock_items</c>, so this contract is the whole of their access.
/// It is the mirror of <c>IProductCatalog</c> and <c>IVendorDirectory</c>, and exists for the same
/// reason.
/// </para>
/// <para>
/// <see cref="HoldAsync"/> is the oversell boundary of this platform. It is atomic per stock item
/// and it either holds every requested unit or holds none, so two checkouts racing for the last
/// unit produce one hold and one refusal rather than two holds and an apology
/// (docs/02-domain-model.md §4.2).
/// </para>
/// <para>
/// A hold is time-limited on purpose. Stock held by a checkout that was abandoned is stock nobody
/// can buy, so a hold carries a TTL and the module sweeps expired ones back into supply.
/// </para>
/// </remarks>
public interface IStockAvailability
{
    /// <summary>What is available against one offer, aggregated across warehouses.</summary>
    /// <param name="listingId">The offer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<StockAvailability> FindAsync(Guid listingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What is available against several offers at once, keyed by listing. Every requested id is
    /// present: an offer Inventory has never heard of comes back untracked rather than missing,
    /// because "no stock row" is an answer a cart has to render.
    /// </summary>
    /// <param name="listingIds">The offers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyDictionary<Guid, StockAvailability>> FindManyAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Holds <paramref name="quantity"/> units of an offer for a cart or an order, expiring at
    /// <paramref name="expiresAt"/>. Returns the reservation id, or null when there was not enough
    /// to hold.
    /// </summary>
    /// <remarks>
    /// Idempotent on <c>(referenceType, referenceId, lineReferenceId)</c>: a retried checkout finds
    /// its own live hold and gets that one back rather than holding the stock a second time.
    /// </remarks>
    /// <param name="listingId">The offer.</param>
    /// <param name="quantity">How many units to hold. Must be positive.</param>
    /// <param name="referenceType">What is holding it — <c>cart</c> or <c>order</c>.</param>
    /// <param name="referenceId">The cart or order.</param>
    /// <param name="lineReferenceId">The line within it, so two lines of one cart hold separately.</param>
    /// <param name="expiresAt">When the hold lapses if nobody settles it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<Guid?> HoldAsync(
        Guid listingId,
        int quantity,
        string referenceType,
        Guid referenceId,
        Guid lineReferenceId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Settles every live hold made against one cart or order: commits them into sales, or
    /// releases them back into supply.
    /// </summary>
    /// <remarks>
    /// Idempotent, because the events that drive it are delivered at least once. A second call
    /// finds no held reservations and reports zero.
    /// </remarks>
    /// <param name="referenceType">What held the stock — <c>cart</c> or <c>order</c>.</param>
    /// <param name="referenceId">The cart or order.</param>
    /// <param name="outcome">Whether the sale happened.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many reservations were settled by this call.</returns>
    ValueTask<int> SettleAsync(
        string referenceType,
        Guid referenceId,
        ReservationOutcome outcome,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The <c>reference_type</c> values a reservation may carry, so the two modules that write them
/// and the one that reads them cannot spell them differently.
/// </summary>
public static class ReservationReferenceTypes
{
    /// <summary>A shopper's cart, held while they are in checkout.</summary>
    public const string Cart = "cart";

    /// <summary>A placed order, held until it is confirmed or cancelled.</summary>
    public const string Order = "order";
}
