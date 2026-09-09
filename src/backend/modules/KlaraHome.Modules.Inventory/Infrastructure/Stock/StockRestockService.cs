using KlaraHome.Contracts.Inventory;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Inventory.Infrastructure.Stock;

/// <summary>
/// Answers <see cref="IStockRestock"/> from this module's own ledger.
/// </summary>
/// <remarks>
/// <para>
/// The seam goods come back through, declared at Step 17 for the Returns module and implemented here
/// because <c>inventory.stock_ledger_entries</c> is append-only and has exactly one writer. A second
/// writer would be a second, unreconcilable account of what this platform has.
/// </para>
/// <para>
/// Idempotency is the ledger's own. Every movement carries the document that caused it, and a
/// reference already present against a listing takes nothing — which is what makes a redelivered QC
/// event, a retried request and an operator clicking twice all move the units exactly once. It is
/// checked per listing rather than per call, because a batch that failed halfway must be able to
/// finish the rest on the retry.
/// </para>
/// <para>
/// A quarantined line moves no stock at all and that is deliberate: the goods are physically present
/// and commercially undecided, and putting them back on sale or writing them off would both be a
/// lie. It writes no ledger entry either — <c>ck_stock_ledger_entries_moves_something</c> refuses a
/// row that moves neither column, so a decision that moves nothing cannot be put on the one table
/// that only records movement. A stock take that finds a quarantined line unaccounted for has
/// nowhere in this ledger to learn why; the disposition itself, on the return line, is the only
/// record today (Step 29 <c>TEST_DEBT.md</c>).
/// </para>
/// </remarks>
/// <param name="context">The Inventory data context.</param>
/// <param name="ledger">The single place a movement is applied.</param>
/// <param name="logger">Reports what moved.</param>
internal sealed partial class StockRestockService(
    InventoryDbContext context,
    StockLedgerService ledger,
    ILogger<StockRestockService> logger) : IStockRestock
{
    /// <inheritdoc />
    public async ValueTask<int> RestockAsync(
        IReadOnlyCollection<RestockUnits> units,
        string referenceType,
        Guid referenceId,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceType);

        if (units.Count == 0)
        {
            return 0;
        }

        var moved = 0;

        // The raw movement inside ApplyAsync commits the instant it runs, and the ledger entry that
        // justifies it is only added to the change tracker — so without an ambient transaction a
        // failure partway through this batch, or in the SaveChangesAsync below, leaves units already
        // moved with no ledger entry to explain them, and the idempotency check above would then
        // treat them as never having happened. One transaction around the whole batch is what makes
        // "moved but not yet saved" impossible to observe from outside this call, and the "already
        // moved" read is taken fresh inside it so a retried attempt sees only what the previous
        // attempt actually committed, not what it merely tried.
        await context.ExecuteInTransactionAsync(
            async (_, token) =>
            {
                // Which stock rows this document has already moved. One query rather than one per
                // line, and the vendor filter is bypassed because a return is settled by the
                // platform, not by the seller whose goods they are. The ledger keys on the stock row
                // rather than on the listing, so the comparison happens against the row this call is
                // about to write to.
                var already = await context.LedgerEntries
                    .AsNoTracking()
                    .IgnoreQueryFilters([ModelConventions.VendorFilter])
                    .Where(entry => entry.ReferenceType == referenceType && entry.ReferenceId == referenceId)
                    .Select(entry => entry.StockItemId)
                    .Distinct()
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                var settled = already.ToHashSet();

                moved = 0;

                foreach (var unit in units)
                {
                    if (unit.Quantity <= 0)
                    {
                        continue;
                    }

                    moved += await ApplyAsync(unit, settled, referenceType, referenceId, note, token)
                        .ConfigureAwait(false);
                }

                if (moved > 0)
                {
                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                }
            },
            cancellationToken)
            .ConfigureAwait(false);

        if (moved > 0)
        {
            UnitsMoved(logger, referenceType, referenceId, moved);
        }

        return moved;
    }

    /// <summary>
    /// Moves one listing's units into the location they should go back to.
    /// </summary>
    /// <remarks>
    /// The highest-priority active location holding this listing, which is where a receipt would go
    /// and where the next sale will be picked from. A listing with no stock row at all is skipped
    /// rather than created — an offer nobody ever stocked is not one a return can invent supply for,
    /// and a silent new row would be a location nobody chose.
    /// </remarks>
    private async Task<int> ApplyAsync(
        RestockUnits unit,
        HashSet<Guid> settled,
        string referenceType,
        Guid referenceId,
        string? note,
        CancellationToken cancellationToken)
    {
        var item = await context.StockItems
            .IgnoreQueryFilters([ModelConventions.VendorFilter])
            .Where(candidate => candidate.ListingId == unit.ListingId)
            .Join(
                context.Warehouses.AsNoTracking().IgnoreQueryFilters([ModelConventions.VendorFilter]),
                candidate => candidate.WarehouseId,
                warehouse => warehouse.Id,
                (candidate, warehouse) => new { Item = candidate, warehouse.Priority, warehouse.Code, warehouse.IsActive })
            .Where(row => row.IsActive)
            .OrderBy(row => row.Priority)
            .ThenBy(row => row.Code)
            .Select(row => row.Item)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            NoLocation(logger, unit.ListingId, referenceType);
            return 0;
        }

        // Already moved under this document. The retry finishes the lines that had not landed and
        // leaves the ones that had exactly where they are.
        if (!settled.Add(item.Id))
        {
            return 0;
        }

        var (change, reason) = unit.Disposition switch
        {
            // Back on sale.
            RestockDisposition.Restock => (unit.Quantity, StockMovementReason.Return),

            // Written off supply. It never went back on, so this is a movement of zero against a
            // location whose count did not change — the entry exists to say the units were accounted
            // for and destroyed, which is what a stock take needs to reconcile against.
            RestockDisposition.Scrap => (0, StockMovementReason.Damage),

            // Held, and neither.
            _ => (0, StockMovementReason.Adjustment),
        };

        var result = await ledger
            .MoveAsync(
                item,
                change,
                reason,
                referenceType,
                referenceId,
                note ?? $"{unit.Quantity} unit(s) {unit.Disposition.ToString().ToLowerInvariant()}",
                actorId: null,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Applied ? unit.Quantity : 0;
    }

    [LoggerMessage(EventId = 1180, Level = LogLevel.Information,
        Message = "{Units} unit(s) came back on {ReferenceType} {ReferenceId}.")]
    private static partial void UnitsMoved(
        ILogger logger,
        string referenceType,
        Guid referenceId,
        int units);

    [LoggerMessage(EventId = 1181, Level = LogLevel.Warning,
        Message = "Listing {ListingId} has no active stock location, so nothing was put back for "
                  + "{ReferenceType}.")]
    private static partial void NoLocation(ILogger logger, Guid listingId, string referenceType);
}
