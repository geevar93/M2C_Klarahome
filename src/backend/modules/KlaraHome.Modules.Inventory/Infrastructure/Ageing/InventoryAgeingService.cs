using KlaraHome.Contracts.Inventory;
using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Inventory.Infrastructure.Ageing;

/// <summary>
/// Answers <see cref="IInventoryAgeing"/> from the stock ledger.
/// </summary>
/// <remarks>
/// <para>
/// This module's first outward read seam, and it exists because ageing is the one inventory number
/// that no sequence of <c>StockLevelChanged</c> messages can reconstruct. Those say what the balance
/// is; the question a buyer asks is when the units currently sitting there arrived, and only the
/// ledger knows that.
/// </para>
/// <para>
/// Age is measured from the last <em>inbound</em> movement, and which movements count as inbound is
/// decided here rather than by looking at the sign of the change. A correction that adds two units
/// because a stock take found them is not a delivery, and dating a line's age from it would reset
/// the clock on goods that have not moved in a year — which is precisely the line the report exists
/// to surface. Only a purchase receipt, a transfer in and a customer return are arrivals.
/// </para>
/// <para>
/// One query per page over a ledger this platform partitions monthly, grouped down to a single row
/// per stock line. That is deliberately not one query per stock item: a walk over a large catalogue
/// doing that would issue tens of thousands of round trips against the highest-volume table in the
/// system.
/// </para>
/// </remarks>
/// <param name="context">The Inventory data context.</param>
/// <param name="clock">The sanctioned clock; ageing is measured against it.</param>
internal sealed class InventoryAgeingService(InventoryDbContext context, IClock clock) : IInventoryAgeing
{
    /// <summary>The movements that mean stock arrived, and therefore reset a line's age.</summary>
    /// <remarks>
    /// An adjustment and a correction are absent on purpose. Both can raise the count and neither is
    /// a delivery — one is somebody fixing a number, the other is a stock take reconciling to what is
    /// physically on the shelf, and the goods it finds have been there all along.
    /// </remarks>
    private static readonly StockMovementReason[] InboundReasons =
    [
        StockMovementReason.Purchase,
        StockMovementReason.TransferIn,
        StockMovementReason.Return,
    ];

    /// <summary>The movements that mean stock left.</summary>
    private static readonly StockMovementReason[] OutboundReasons =
    [
        StockMovementReason.Sale,
        StockMovementReason.TransferOut,
        StockMovementReason.Damage,
    ];

    /// <inheritdoc />
    public async ValueTask<StockAgePage> EnumerateAsync(
        Guid? afterListingId,
        int size,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(size, 1, 500);

        var items = context.StockItems.AsNoTracking().Where(item => item.QuantityOnHand > 0);

        if (afterListingId is { } cursor)
        {
            items = items.Where(item => item.ListingId.CompareTo(cursor) > 0);
        }

        // Paged on the listing rather than on the row's own id, because the listing is what the
        // caller passes back and what the contract's cursor is defined as. A stock line is unique per
        // listing and warehouse, so a listing stocked in two places produces two rows on the same
        // page — which is what a per-location ageing report wants.
        var page = await items
            .OrderBy(item => item.ListingId)
            .Take(take + 1)
            .Join(
                context.Warehouses.AsNoTracking(),
                item => item.WarehouseId,
                warehouse => warehouse.Id,
                (item, warehouse) => new { Item = item, Warehouse = warehouse })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > take;
        var rows = page.Take(take).ToList();

        if (rows.Count == 0)
        {
            return new StockAgePage([], null);
        }

        var stockItemIds = rows.Select(row => row.Item.Id).ToArray();

        // One grouped pass over the ledger for both dates. Reading them separately would be two scans
        // of the same partitions for facts that come out of the same rows.
        var movements = await context.LedgerEntries
            .AsNoTracking()
            .Where(entry => stockItemIds.Contains(entry.StockItemId))
            .GroupBy(entry => entry.StockItemId)
            .Select(group => new
            {
                StockItemId = group.Key,
                LastInboundAt = group
                    .Where(entry => InboundReasons.Contains(entry.Reason))
                    .Max(entry => (DateTimeOffset?)entry.OccurredAt),
                LastOutboundAt = group
                    .Where(entry => OutboundReasons.Contains(entry.Reason))
                    .Max(entry => (DateTimeOffset?)entry.OccurredAt),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byStockItem = movements.ToDictionary(row => row.StockItemId);
        var now = clock.UtcNow;

        var snapshots = rows
            .Select(row =>
            {
                byStockItem.TryGetValue(row.Item.Id, out var dates);

                var lastInbound = dates?.LastInboundAt;

                return new StockAgeSnapshot(
                    row.Item.ListingId,
                    row.Item.WarehouseId,
                    row.Warehouse.Name,
                    row.Item.VendorId,
                    row.Item.Sku,
                    row.Item.QuantityOnHand,
                    row.Item.QuantityReserved,
                    lastInbound,
                    dates?.LastOutboundAt,
                    // Null, not zero, when nothing ever arrived. A line whose stock was opened by an
                    // adjustment has an unknown age, and reporting it as brand new would put the
                    // oldest goods in the store at the top of the "freshest" list.
                    lastInbound is null ? null : Math.Max(0, (int)(now - lastInbound.Value).TotalDays));
            })
            .ToList();

        return new StockAgePage(snapshots, hasMore ? snapshots[^1].ListingId : null);
    }
}
