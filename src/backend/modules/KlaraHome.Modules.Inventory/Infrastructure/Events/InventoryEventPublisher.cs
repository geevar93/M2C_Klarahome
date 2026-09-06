using KlaraHome.Contracts.Inventory;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Inventory.Infrastructure.Events;

/// <summary>
/// Announces what happened to stock (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// The outbox is resolved <b>keyed by this module's context</b>, and that is not decoration. The
/// unkeyed registration is first-wins and belongs to whichever module registered first; enqueuing
/// through it here would add the row to a different context's change tracker, this module's
/// <c>SaveChangesAsync</c> would not write it, and the event would be lost with no error anywhere.
/// </para>
/// <para>
/// Nothing is saved here. The event becomes real when the caller's transaction commits, and not
/// before — which is the point of the pattern (ADR-003), and matters more in this module than in
/// most: an availability event published for a movement that was then rolled back would leave the
/// search index advertising stock that does not exist.
/// </para>
/// </remarks>
/// <param name="outbox">This module's outbox, keyed by its context.</param>
internal sealed class InventoryEventPublisher(
    [FromKeyedServices(typeof(InventoryDbContext))] IOutbox outbox)
{
    /// <summary>The stock behind an offer moved.</summary>
    /// <param name="item">The stock row, already carrying the post-movement balances.</param>
    /// <param name="wasAvailable">Whether anything was available before.</param>
    public void StockChanged(StockItem item, bool wasAvailable)
    {
        ArgumentNullException.ThrowIfNull(item);

        outbox.Enqueue(new StockLevelChanged(
            item.ListingId,
            item.VendorId ?? Guid.Empty,
            item.QuantityOnHand,
            item.QuantityAvailable,
            wasAvailable,
            item.IsAvailable));
    }

    /// <summary>A stock item crossed below its reorder level.</summary>
    /// <param name="item">The stock row.</param>
    public void RunningLow(StockItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        outbox.Enqueue(new StockRunningLow(
            item.Id,
            item.ListingId,
            item.VendorId ?? Guid.Empty,
            item.WarehouseId,
            item.Sku,
            item.QuantityAvailable,
            item.ReorderLevel,
            item.ReorderQuantity));
    }
}
