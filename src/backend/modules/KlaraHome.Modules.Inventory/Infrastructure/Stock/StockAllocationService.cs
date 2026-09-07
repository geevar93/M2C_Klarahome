using KlaraHome.Contracts.Inventory;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Inventory.Infrastructure.Stock;

/// <summary>
/// Answers where the units behind a hold are (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// Reads only, and no vendor filter. The caller is a back-office surface that has already been
/// scoped by its own module — an order's lines, a seller's parcels — and applying this module's
/// vendor filter on top would make a platform operator's pick list empty for every seller.
/// </para>
/// <para>
/// Held and settled reservations both count. By the time anybody asks where a parcel's units came
/// from, the hold has been committed; a query that only saw live holds would answer "nowhere" for
/// exactly the orders being packed.
/// </para>
/// </remarks>
/// <param name="context">The Inventory data context.</param>
internal sealed class StockAllocationService(InventoryDbContext context) : IStockAllocation
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<StockAllocation>> GetAllocationsAsync(
        string referenceType,
        Guid referenceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(referenceType) || referenceId == Guid.Empty)
        {
            return [];
        }

        var rows = await context.Reservations
            .AsNoTracking()
            .IgnoreQueryFilters([ModelConventions.VendorFilter])
            .Where(reservation => reservation.ReferenceType == referenceType
                                  && reservation.ReferenceId == referenceId)
            .Join(
                context.StockItems.AsNoTracking().IgnoreQueryFilters([ModelConventions.VendorFilter]),
                reservation => reservation.StockItemId,
                item => item.Id,
                (reservation, item) => new { reservation, item.WarehouseId })
            .Join(
                context.Warehouses.AsNoTracking().IgnoreQueryFilters([ModelConventions.VendorFilter]),
                row => row.WarehouseId,
                warehouse => warehouse.Id,
                (row, warehouse) => new StockAllocation(
                    row.reservation.ListingId,
                    row.reservation.LineReferenceId,
                    warehouse.Id,
                    warehouse.Code,
                    warehouse.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WarehouseSummary>> GetWarehousesAsync(
        IReadOnlyCollection<Guid> warehouseIds,
        CancellationToken cancellationToken = default)
    {
        if (warehouseIds is null || warehouseIds.Count == 0)
        {
            return [];
        }

        var ids = warehouseIds.Distinct().ToList();

        var rows = await context.Warehouses
            .AsNoTracking()
            .IgnoreQueryFilters([ModelConventions.VendorFilter])
            .Where(warehouse => ids.Contains(warehouse.Id))
            .Select(warehouse => new WarehouseSummary(warehouse.Id, warehouse.Code, warehouse.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows;
    }
}
