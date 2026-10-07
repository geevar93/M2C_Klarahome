using KlaraHome.Contracts.Inventory;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Inventory.Infrastructure.Stock;

/// <summary>
/// Answers <see cref="IStockAlerts"/> from the stock table.
/// </summary>
/// <remarks>
/// The expression restates <c>StockItem.IsLow</c> in SQL, as the stock list's low-stock filter does,
/// because EF cannot translate the property. Written the same way in both places on purpose - they
/// have to say the same thing, and the dashboard count and the list it links to disagreeing would
/// be the first thing anybody noticed.
/// </remarks>
/// <param name="context">The Inventory data context.</param>
internal sealed class StockAlertService(InventoryDbContext context) : IStockAlerts
{
    /// <inheritdoc />
    public Task<int> CountLowStockAsync(CancellationToken cancellationToken = default)
        => context.StockItems
            .AsNoTracking()
            .CountAsync(
                item => item.ReorderLevel > 0
                        && item.QuantityOnHand - item.QuantityReserved <= item.ReorderLevel,
                cancellationToken);
}
