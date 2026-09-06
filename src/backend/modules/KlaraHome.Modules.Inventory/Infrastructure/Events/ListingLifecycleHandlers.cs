using KlaraHome.Contracts.Catalog;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Inventory.Infrastructure.Events;

/// <summary>
/// Opens stock rows for offers as they go live, and keeps their labels honest
/// (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// A listing that goes live with no stock row is an offer whose availability nobody can answer: the
/// contract would report it untracked, the storefront would refuse to sell it, and a seller would
/// have to notice and fix it by hand. So the module reacts to <see cref="ListingPublished"/> and
/// opens the row at zero, in the seller's default location.
/// </para>
/// <para>
/// Zero rather than one, deliberately. Opening a row does not invent stock; it makes the offer
/// countable, and the seller's first goods receipt or adjustment puts real units in it.
/// </para>
/// <para>
/// Delivery is at-least-once, so every method here is idempotent — a redelivered publication finds
/// the row already open and does nothing.
/// </para>
/// <para>
/// Deactivation deliberately moves no stock. An offer that has been paused still has units on a
/// shelf, they are still the seller's, and they are still there when the offer comes back. What
/// stops them being sold is the listing's own status, which Catalog owns.
/// </para>
/// </remarks>
/// <param name="context">The Inventory data context.</param>
/// <param name="catalog">Reads the offer's current SKU, over the contract rather than a join.</param>
/// <param name="logger">Reports what was opened.</param>
internal sealed partial class ListingLifecycleHandlers(
    InventoryDbContext context,
    IProductCatalog catalog,
    ILogger<ListingLifecycleHandlers> logger)
    : IIntegrationEventHandler<ListingPublished>,
        IIntegrationEventHandler<ListingUpdated>,
        IIntegrationEventHandler<ListingDeactivated>
{
    /// <inheritdoc />
    public async Task HandleAsync(ListingPublished integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var exists = await context.StockItems
            .AnyAsync(item => item.ListingId == integrationEvent.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return;
        }

        // The seller's own default location, or the platform's, in the order an allocator would
        // walk them. A seller with no warehouse at all gets no stock row and a log line — they have
        // to tell us where their goods are before we can count them.
        var warehouse = await context.Warehouses
            .Where(candidate => candidate.IsActive
                                && (candidate.VendorId == integrationEvent.VendorId
                                    || candidate.VendorId == null))
            .OrderBy(candidate => candidate.VendorId == null ? 1 : 0)
            .ThenBy(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Code)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (warehouse is null)
        {
            NoWarehouse(logger, integrationEvent.ListingId, integrationEvent.VendorId);
            return;
        }

        context.StockItems.Add(StockItem.Open(
            integrationEvent.ListingId,
            warehouse.Id,
            integrationEvent.VendorId,
            integrationEvent.Sku));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        StockOpened(logger, integrationEvent.ListingId, warehouse.Code);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The offer's price and terms are none of this module's business. What is, is the denormalised
    /// SKU: it is copied here so a stock screen can be read without a query across a schema, and a
    /// copy nobody refreshes is a label that quietly stops matching the goods.
    /// </remarks>
    public async Task HandleAsync(ListingUpdated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var items = await context.StockItems
            .Where(item => item.ListingId == integrationEvent.ListingId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (items.Count == 0)
        {
            return;
        }

        // The event does not carry the SKU — it is about price and terms — so the current value is
        // read over the contract rather than joined for. One call for a listing that has already
        // been established as interesting is cheap; a cross-schema join would not be allowed at any
        // price (docs/01-architecture.md §2.1).
        var summary = await catalog
            .FindListingAsync(integrationEvent.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (summary is null)
        {
            return;
        }

        foreach (var item in items)
        {
            item.RenameSku(summary.Sku);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task HandleAsync(ListingDeactivated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // Nothing to do, and that is the decision rather than an omission: a paused offer keeps its
        // stock, because the units are still on the shelf and still the seller's.
        ListingWithdrawn(logger, integrationEvent.ListingId);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 7130,
        Level = LogLevel.Information,
        Message = "Opened a stock item for listing {ListingId} at warehouse {WarehouseCode}")]
    private static partial void StockOpened(ILogger logger, Guid listingId, string warehouseCode);

    [LoggerMessage(
        EventId = 7131,
        Level = LogLevel.Warning,
        Message = "Listing {ListingId} went live but seller {VendorId} has no active warehouse, "
                  + "so its stock cannot be tracked")]
    private static partial void NoWarehouse(ILogger logger, Guid listingId, Guid vendorId);

    [LoggerMessage(
        EventId = 7132,
        Level = LogLevel.Debug,
        Message = "Listing {ListingId} left the storefront; its stock is unchanged")]
    private static partial void ListingWithdrawn(ILogger logger, Guid listingId);
}
