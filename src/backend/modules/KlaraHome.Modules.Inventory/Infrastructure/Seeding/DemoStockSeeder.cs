using KlaraHome.Contracts.Catalog;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using KlaraHome.Modules.Inventory.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Inventory.Infrastructure.Seeding;

/// <summary>
/// Puts stock behind the demonstration offers, so the demo storefront is a shop rather than a
/// catalogue of things that cannot be bought.
/// </summary>
/// <remarks>
/// <para>
/// **Without this every demo product renders as unavailable, which looks like a defect.** The buy
/// box is relaxed about unknown stock — it ranks it as "do not discriminate" rather than as
/// unavailable — but the search projection is not: it asks <c>IStockAvailability</c> and writes
/// <c>is_available = false</c> for an offer with no stock row. The storefront then greys the card,
/// dims the image and refuses the add-to-cart, on all ten products at once. A reviewer looking at
/// that is looking at a bug report, not a demonstration.
/// </para>
/// <para>
/// **The stock is moved through the ledger, not written onto the row.** <c>QuantityOnHand</c> has a
/// private setter for a reason: on-hand is the sum of its movements, and a seeder that set the
/// number directly would produce an item whose quantity no ledger entry explains — which is exactly
/// the state a stock take exists to find and an auditor exists to ask about. So it opens the item at
/// zero and receives into it, and the resulting ledger reads the way a real opening balance does.
/// </para>
/// <para>
/// **It finds the demonstration offers through the published catalogue contract**, the same seam
/// the Search module walks the catalogue with. Inventory may not read the Catalog schema
/// (docs/01-architecture.md §2.1), and the <c>DEMO-</c> SKU prefix is what tells it which of the
/// offers it walks past are the ones it is here for.
/// </para>
/// <para>
/// See <see cref="DemoDataOptions"/> for why a demo seeder exists at all and what fences it.
/// </para>
/// </remarks>
/// <param name="context">The Inventory data context.</param>
/// <param name="catalogue">Walks the catalogue for the offers to stock.</param>
/// <param name="ledger">Writes the movement and syncs the quantities in one transaction.</param>
/// <param name="environment">Refuses to run in Production whatever configuration says.</param>
/// <param name="logger">Reports what was stocked.</param>
internal sealed partial class DemoStockSeeder(
    InventoryDbContext context,
    IProductProjectionSource catalogue,
    StockLedgerService ledger,
    IHostEnvironment environment,
    ILogger<DemoStockSeeder> logger) : IDataSeeder
{
    /// <summary>The prefix every demonstration SKU carries.</summary>
    private const string DemoSkuPrefix = "DEMO-";

    /// <summary>The demonstration warehouse's code.</summary>
    private const string DemoWarehouseCode = "DEMO-WH-JAI";

    /// <summary>
    /// Units received against each demonstration offer.
    /// </summary>
    /// <remarks>
    /// Comfortably more than the listings' <c>maxOrderQuantity</c> of five, so a reviewer can add
    /// several of everything to a basket and check out repeatedly without walking the demo into an
    /// out-of-stock state that then needs re-seeding to escape.
    /// </remarks>
    private const int OpeningBalance = 120;

    /// <summary>How many offers to walk in one page of the catalogue.</summary>
    private const int PageSize = 200;

    /// <inheritdoc />
    public string Name => "Inventory.DemoStock";

    /// <summary>
    /// After the demonstration catalogue, whose offers it stocks, and before the search projection,
    /// which reads the stock this writes.
    /// </summary>
    public int Order => 915;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // The second fence. See DemoVendorSeeder for why registration alone is not enough.
        if (environment.IsProduction())
        {
            return;
        }

        var offers = await FindDemoOffersAsync(cancellationToken).ConfigureAwait(false);

        if (offers.Count == 0)
        {
            return;
        }

        var listingIds = offers.Select(offer => offer.ListingId).ToList();

        var alreadyStocked = await context.StockItems
            .Where(item => listingIds.Contains(item.ListingId))
            .Select(item => item.ListingId)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);

        var outstanding = offers.Where(offer => !alreadyStocked.Contains(offer.ListingId)).ToList();

        if (outstanding.Count == 0)
        {
            return;
        }

        var warehouseId = await EnsureWarehouseAsync(outstanding[0].VendorId, cancellationToken)
            .ConfigureAwait(false);

        var items = new List<StockItem>(outstanding.Count);

        foreach (var offer in outstanding)
        {
            var item = StockItem.Open(offer.ListingId, warehouseId, offer.VendorId, offer.Sku);

            item.Configure(
                reorderLevel: 10,
                reorderQuantity: 50,
                allowBackorder: false,
                allowPreorder: false,
                preorderAvailableAt: null,
                // A count and nothing else. Cushion covers and stoneware carry no batch or serial,
                // and tracking one would demonstrate a workflow these goods do not have.
                StockTrackingMode.None);

            items.Add(item);
            context.StockItems.Add(item);
        }

        // Saved before the movements, because the ledger updates the rows by id and cannot move
        // stock into an item that is not yet in the table.
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var item in items)
        {
            await ledger
                .MoveAsync(
                    item,
                    OpeningBalance,
                    StockMovementReason.Adjustment,
                    referenceType: "demo-seed",
                    referenceId: null,
                    note: "Opening balance for the demonstration catalogue.",
                    actorId: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        DemoStockSeeded(logger, items.Count, OpeningBalance);
    }

    /// <summary>Walks the catalogue and keeps the buy-box offer of every demonstration SKU.</summary>
    private async Task<List<ProductProjection>> FindDemoOffersAsync(CancellationToken cancellationToken)
    {
        var found = new List<ProductProjection>();
        Guid? cursor = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var page = await catalogue.EnumerateAsync(cursor, PageSize, cancellationToken).ConfigureAwait(false);

            found.AddRange(page.Items.Where(offer =>
                offer.Sku.StartsWith(DemoSkuPrefix, StringComparison.OrdinalIgnoreCase)));

            if (page.NextVariantCursor is null)
            {
                break;
            }

            cursor = page.NextVariantCursor;
        }

        return found;
    }

    /// <summary>The demonstration warehouse, created on first run.</summary>
    private async Task<Guid> EnsureWarehouseAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        var existing = await context.Warehouses
            .FirstOrDefaultAsync(warehouse => warehouse.Code == DemoWarehouseCode, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.Id;
        }

        // Owned by the demo seller rather than by the platform, because that is the shape this
        // marketplace actually takes — a platform-owned warehouse would demonstrate the wrong model.
        var warehouse = Warehouse.Open(vendorId, DemoWarehouseCode, "Jaipur (demonstration)", "302022");

        warehouse.Update(
            "Jaipur (demonstration)",
            "302022",
            new WarehouseAddress
            {
                Line1 = "Plot 14, Sitapura Industrial Area",
                City = "Jaipur",
            },
            priority: 0);

        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return warehouse.Id;
    }

    [LoggerMessage(
        EventId = 9104,
        Level = LogLevel.Information,
        Message = "Stocked {Count} demonstration offer(s) with {Quantity} units each.")]
    private static partial void DemoStockSeeded(ILogger logger, int count, int quantity);
}
