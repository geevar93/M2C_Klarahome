namespace KlaraHome.Contracts.Inventory;

/// <summary>Which warehouse a hold actually came out of.</summary>
/// <param name="ListingId">The offer the units were held against.</param>
/// <param name="LineReferenceId">The cart or order line that holds them.</param>
/// <param name="WarehouseId">The location the units are in.</param>
/// <param name="WarehouseCode">Its short code, for a label or a pick list header.</param>
/// <param name="WarehouseName">What it is called.</param>
public sealed record StockAllocation(
    Guid ListingId,
    Guid LineReferenceId,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName);

/// <summary>One stock location, as another module needs to name it.</summary>
/// <param name="Id">The warehouse.</param>
/// <param name="Code">Its short code.</param>
/// <param name="Name">What it is called.</param>
public sealed record WarehouseSummary(Guid Id, string Code, string Name);

/// <summary>
/// Where the units behind a hold are (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// Separate from <c>IStockAvailability</c>, which aggregates across warehouses on purpose: a cart,
/// a product page and a checkout should not know or care where a unit is. This contract is for the
/// two callers that must — the order that records where its units were taken from, and the pick
/// list that sends somebody to a shelf.
/// </para>
/// <para>
/// It answers about a <em>reference</em> — a cart or an order — rather than about a listing,
/// because "where is this offer stocked" and "where did these particular units come from" are
/// different questions and only the second one has one answer.
/// </para>
/// </remarks>
public interface IStockAllocation
{
    /// <summary>
    /// Where the units held against one cart or order were taken from, live holds and settled ones
    /// alike.
    /// </summary>
    /// <remarks>
    /// Settled holds are included deliberately. By the time a parcel is packed the hold has been
    /// committed, and a query that only saw live holds would answer "nowhere" for every order that
    /// had actually been placed.
    /// </remarks>
    /// <param name="referenceType">The kind of holder: <c>cart</c> or <c>order</c>.</param>
    /// <param name="referenceId">The cart or the order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<StockAllocation>> GetAllocationsAsync(
        string referenceType,
        Guid referenceId,
        CancellationToken cancellationToken = default);

    /// <summary>Names some warehouses, so another module can label an id it is carrying.</summary>
    /// <param name="warehouseIds">The locations to name. An unknown id is simply absent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<WarehouseSummary>> GetWarehousesAsync(
        IReadOnlyCollection<Guid> warehouseIds,
        CancellationToken cancellationToken = default);
}
