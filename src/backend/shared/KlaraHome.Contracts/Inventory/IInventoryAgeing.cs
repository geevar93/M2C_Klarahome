namespace KlaraHome.Contracts.Inventory;

/// <summary>
/// How long one line of stock has been sitting where it is.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AgeDays"/> is measured from the last <em>inbound</em> movement, not from the row's
/// creation. A stock line that is replenished weekly is not old however long the row has existed,
/// and a line that was received once in March and has not moved since is exactly the number a buyer
/// wants to see in September.
/// </para>
/// <para>
/// There is no value here, and deliberately. What stock is worth depends on which cost basis a
/// business uses, and this platform does not keep one; a caller that wants money multiplies by a
/// price it has its own opinion about.
/// </para>
/// </remarks>
/// <param name="ListingId">The offer the stock is held against.</param>
/// <param name="WarehouseId">Where it is.</param>
/// <param name="WarehouseName">What that place is called, so a report needs no second lookup.</param>
/// <param name="VendorId">Whose stock it is, or null for the platform's own.</param>
/// <param name="Sku">The stock-keeping unit.</param>
/// <param name="QuantityOnHand">How much is physically there.</param>
/// <param name="QuantityReserved">How much of it is already promised to an order.</param>
/// <param name="LastInboundAt">The last time stock arrived, or null if it never has.</param>
/// <param name="LastOutboundAt">The last time stock left, or null if it never has.</param>
/// <param name="AgeDays">
/// Days since <paramref name="LastInboundAt"/>, or null when there has been no inbound movement at
/// all — which is a different statement from "zero days old" and must not be rendered as one.
/// </param>
public sealed record StockAgeSnapshot(
    Guid ListingId,
    Guid WarehouseId,
    string WarehouseName,
    Guid? VendorId,
    string Sku,
    int QuantityOnHand,
    int QuantityReserved,
    DateTimeOffset? LastInboundAt,
    DateTimeOffset? LastOutboundAt,
    int? AgeDays);

/// <summary>One page of a stock walk, and where to resume it.</summary>
/// <param name="Items">The stock lines on this page.</param>
/// <param name="NextCursor">
/// The last stock line on this page, to be passed back as the cursor, or null when the walk is over.
/// </param>
public sealed record StockAgePage(IReadOnlyList<StockAgeSnapshot> Items, Guid? NextCursor);

/// <summary>
/// Reads how long stock has been sitting, from outside the Inventory module
/// (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// Added at Step 21 for the stock-ageing report. Ageing is the one number in that report which
/// cannot be assembled from integration events: <c>StockLevelChanged</c> says what the balance is
/// now, and no sequence of those messages tells you when the units currently on the shelf arrived.
/// The answer is in the stock ledger, which belongs to Inventory, so Inventory answers it.
/// </para>
/// <para>
/// It is a walk rather than a query, and that shape is the point. The caller is a scheduled job
/// taking a snapshot it intends to keep — ageing is a series, not a state, and a report that could
/// only ever say "as of right now" would be unable to answer whether the position is getting better
/// or worse.
/// </para>
/// </remarks>
public interface IInventoryAgeing
{
    /// <summary>
    /// Walks the stock lines that hold something, oldest arrival first within a page.
    /// </summary>
    /// <remarks>
    /// Lines holding nothing are left out. A stock row at zero has no age worth reporting and there
    /// are far more of them than there are of the rows a buyer is looking for.
    /// </remarks>
    /// <param name="afterListingId">Resume after this stock line, or null to start at the beginning.</param>
    /// <param name="size">How many rows to return. The implementation may return fewer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<StockAgePage> EnumerateAsync(
        Guid? afterListingId,
        int size,
        CancellationToken cancellationToken = default);
}
