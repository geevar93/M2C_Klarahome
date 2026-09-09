using System.Net.Http.Json;
using System.Text.Json;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>An offer with stock on a shelf, and the ids a test needs to move it.</summary>
/// <param name="VendorId">The seller who owns the offer, or null for a platform-owned one.</param>
/// <param name="ProductId">The product behind it.</param>
/// <param name="VariantId">The variant behind it.</param>
/// <param name="ListingId">The offer stock is counted against.</param>
/// <param name="WarehouseId">Where the units are.</param>
/// <param name="StockItemId">The stock row.</param>
/// <param name="Sku">Its SKU, denormalised from the catalogue.</param>
internal sealed record StockedOffer(
    Guid? VendorId,
    Guid ProductId,
    Guid VariantId,
    Guid ListingId,
    Guid WarehouseId,
    Guid StockItemId,
    string Sku);

/// <summary>
/// Builds the warehouses, offers and shelves a Step 11 test stands on, through the API the people
/// who run a warehouse would use.
/// </summary>
/// <remarks>
/// <para>
/// The same reasoning as <see cref="VendorScenario"/> and <see cref="CatalogScenario"/>. Stock
/// written as rows is stock with no ledger behind it, and every one of the invariants this module
/// exists to keep — on hand equals the ledger sum, reserved equals the ledger sum, a movement is one
/// conditional update — is a statement about how the rows were <em>produced</em>. A test that wrote
/// them itself would be asserting its own arithmetic.
/// </para>
/// <para>
/// Units reach a shelf one of two ways here, and both are the product's own. A goods receipt against
/// a purchase order is the inbound path a buyer uses and the one the receipt tests need; an
/// adjustment is what an operator does when there is no supplier involved, and it is the cheaper
/// setup for a test that is about something else. Neither writes a row behind the API's back.
/// </para>
/// <para>
/// Every code it mints carries a fresh suffix. The collection shares one database and a warehouse
/// code is unique per tenant, so two tests opening "the main shelf" must not be opening the same one.
/// </para>
/// </remarks>
/// <param name="admin">A client signed in as platform staff.</param>
/// <param name="cancellationToken">Cancellation token.</param>
internal sealed class InventoryScenario(HttpClient admin, CancellationToken cancellationToken)
{
    /// <summary>The client this scenario drives, for a test that wants to carry on from here.</summary>
    public HttpClient Admin => admin;

    /// <summary>Opens a stock location.</summary>
    /// <param name="vendorId">Whose it is. Null opens a platform location.</param>
    /// <param name="priority">Allocation order. Lower is walked first.</param>
    /// <param name="client">The client to open it through. Defaults to the scenario's own.</param>
    public async Task<Guid> WarehouseAsync(Guid? vendorId = null, int priority = 0, HttpClient? client = null)
    {
        var created = await Rest.ReadAsync(
            await (client ?? admin).PostAsJsonAsync(
                "/api/v1/admin/warehouses",
                new
                {
                    vendorId,
                    code = $"WH{Suffix()}",
                    name = "Test location",
                    pincode = "500034",
                    address = (object?)null,
                    priority,
                },
                cancellationToken),
            cancellationToken);

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>Restates a location — the route a test closes one through.</summary>
    /// <param name="warehouseId">The location.</param>
    /// <param name="priority">Allocation order.</param>
    /// <param name="isActive">Whether stock may still move through it.</param>
    /// <param name="client">The client to act as. Defaults to the scenario's own.</param>
    public Task<HttpResponseMessage> UpdateWarehouseAsync(
        Guid warehouseId,
        int priority = 0,
        bool isActive = true,
        HttpClient? client = null)
        => (client ?? admin).PutAsJsonAsync(
            $"/api/v1/admin/warehouses/{warehouseId}",
            new
            {
                name = "Test location",
                pincode = "500034",
                address = (object?)null,
                priority,
                isActive,
            },
            cancellationToken);

    /// <summary>Adds a supplier goods can be bought from.</summary>
    /// <param name="vendorId">Whose list. Null adds a platform supplier.</param>
    /// <param name="client">The client to act as. Defaults to the scenario's own.</param>
    public async Task<Guid> SupplierAsync(Guid? vendorId = null, HttpClient? client = null)
    {
        var created = await Rest.ReadAsync(
            await (client ?? admin).PostAsJsonAsync(
                "/api/v1/admin/suppliers",
                new
                {
                    vendorId,
                    code = $"SUP{Suffix()}",
                    name = "Panipat Weavers",
                    contactName = "Sales desk",
                    email = "sales@weavers.test",
                    phone = "9876500002",
                    gstin = (string?)null,
                    address = (object?)null,
                    paymentTermsDays = 30,
                },
                cancellationToken),
            cancellationToken);

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>Raises a purchase order and sends it, so goods may be received against it.</summary>
    /// <param name="supplierId">Who it is placed on.</param>
    /// <param name="warehouseId">Where the goods go.</param>
    /// <param name="lines">The offers being bought in, and how many of each.</param>
    /// <param name="submit">Whether to send it. A draft cannot be received against.</param>
    public async Task<(Guid OrderId, IReadOnlyList<Guid> LineIds)> PurchaseOrderAsync(
        Guid supplierId,
        Guid warehouseId,
        IReadOnlyList<(Guid ListingId, int Quantity)> lines,
        bool submit = true)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var created = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/purchase-orders",
                new
                {
                    supplierId,
                    warehouseId,
                    expectedAt = (DateTimeOffset?)null,
                    notes = "Raised by an integration test.",
                    lines = lines.Select(line => new
                    {
                        listingId = line.ListingId,
                        description = "Cotton cushion covers",
                        quantityOrdered = line.Quantity,
                        unitCost = 400m,
                        taxRate = 5m,
                    }).ToArray(),
                },
                cancellationToken),
            cancellationToken);

        var orderId = created.GetProperty("id").GetGuid();

        if (submit)
        {
            await Rest.ReadAsync(
                await admin.PostAsJsonAsync(
                    $"/api/v1/admin/purchase-orders/{orderId}/submit",
                    new { },
                    cancellationToken),
                cancellationToken);
        }

        var lineIds = created.GetProperty("lines").EnumerateArray()
            .Select(line => line.GetProperty("id").GetGuid())
            .ToList();

        return (orderId, lineIds);
    }

    /// <summary>Books goods in against an order. This is what actually moves stock inbound.</summary>
    /// <param name="orderId">The order the goods came against.</param>
    /// <param name="lines">One entry per counted line.</param>
    public Task<HttpResponseMessage> ReceiveAsync(Guid orderId, params object[] lines)
        => admin.PostAsJsonAsync(
            $"/api/v1/admin/purchase-orders/{orderId}/receive",
            new { notes = (string?)null, lines },
            cancellationToken);

    /// <summary>One counted line of a goods receipt.</summary>
    /// <param name="purchaseOrderLineId">The ordered line it satisfies.</param>
    /// <param name="accepted">How many were accepted onto the shelf.</param>
    /// <param name="rejected">How many were refused and sent back.</param>
    /// <param name="rejectionReason">Why they were refused.</param>
    /// <param name="batchCode">The supplier's lot number, for a batch-tracked item.</param>
    public static object ReceiptLine(
        Guid purchaseOrderLineId,
        int accepted,
        int rejected = 0,
        string? rejectionReason = null,
        string? batchCode = null)
        => new
        {
            purchaseOrderLineId,
            accepted,
            rejected,
            rejectionReason,
            batchCode,
            expiresOn = (DateOnly?)null,
        };

    /// <summary>Opens a stock row for an offer at a location, at zero.</summary>
    /// <param name="listingId">The offer.</param>
    /// <param name="warehouseId">Where it will be held.</param>
    /// <param name="client">The client to act as. Defaults to the scenario's own.</param>
    public Task<HttpResponseMessage> OpenStockAsync(
        Guid listingId,
        Guid warehouseId,
        HttpClient? client = null)
        => (client ?? admin).PostAsJsonAsync(
            "/api/v1/admin/stock",
            new { listingId, warehouseId },
            cancellationToken);

    /// <summary>Opens a stock row and answers its id, failing the test if it was refused.</summary>
    /// <param name="listingId">The offer.</param>
    /// <param name="warehouseId">Where it will be held.</param>
    public async Task<Guid> StockItemAsync(Guid listingId, Guid warehouseId)
    {
        var opened = await Rest.ReadAsync(
            await OpenStockAsync(listingId, warehouseId),
            cancellationToken);

        return opened.GetProperty("id").GetGuid();
    }

    /// <summary>Moves stock by hand, with a reason.</summary>
    /// <param name="stockItemId">The stock row.</param>
    /// <param name="change">Signed units.</param>
    /// <param name="reason">Why. Only the four an operator may choose are accepted.</param>
    /// <param name="note">What they wrote.</param>
    /// <param name="client">The client to act as. Defaults to the scenario's own.</param>
    public Task<HttpResponseMessage> AdjustAsync(
        Guid stockItemId,
        int change,
        string reason = "Adjustment",
        string? note = null,
        HttpClient? client = null)
        => (client ?? admin).PostAsJsonAsync(
            "/api/v1/admin/stock/adjustments",
            new { stockItemId, change, reason, note },
            cancellationToken);

    /// <summary>Sets a stock row's replenishment and selling policy.</summary>
    /// <param name="stockItemId">The stock row.</param>
    /// <param name="reorderLevel">The level at or below which to alert. Zero disables it.</param>
    /// <param name="reorderQuantity">How many the seller buys at a time.</param>
    /// <param name="allowBackorder">Whether to accept orders beyond what is on hand.</param>
    /// <param name="allowPreorder">Whether the offer may be sold before release.</param>
    /// <param name="preorderAvailableAt">When a pre-ordered unit is expected to ship.</param>
    /// <param name="trackingMode">How closely individual units are tracked.</param>
    /// <param name="client">The client to act as. Defaults to the scenario's own.</param>
    public Task<HttpResponseMessage> ConfigureAsync(
        Guid stockItemId,
        int reorderLevel = 0,
        int reorderQuantity = 0,
        bool allowBackorder = false,
        bool allowPreorder = false,
        DateTimeOffset? preorderAvailableAt = null,
        string trackingMode = "None",
        HttpClient? client = null)
        => (client ?? admin).PutAsJsonAsync(
            $"/api/v1/admin/stock/{stockItemId}/settings",
            new
            {
                reorderLevel,
                reorderQuantity,
                allowBackorder,
                allowPreorder,
                preorderAvailableAt,
                trackingMode,
            },
            cancellationToken);

    /// <summary>Moves units of one offer between two locations.</summary>
    /// <param name="listingId">The offer.</param>
    /// <param name="fromWarehouseId">Where the units leave.</param>
    /// <param name="toWarehouseId">Where they arrive.</param>
    /// <param name="quantity">How many.</param>
    /// <param name="note">Why.</param>
    public Task<HttpResponseMessage> TransferAsync(
        Guid listingId,
        Guid fromWarehouseId,
        Guid toWarehouseId,
        int quantity,
        string? note = null)
        => admin.PostAsJsonAsync(
            "/api/v1/admin/stock/transfers",
            new { listingId, fromWarehouseId, toWarehouseId, quantity, note },
            cancellationToken);

    /// <summary>Reads one stock row as the admin surface states it.</summary>
    /// <param name="stockItemId">The stock row.</param>
    /// <param name="client">The client to act as. Defaults to the scenario's own.</param>
    public async Task<JsonElement> ReadStockAsync(Guid stockItemId, HttpClient? client = null)
        => await Rest.ReadAsync(
            await (client ?? admin).GetAsync(
                new Uri($"/api/v1/admin/stock/{stockItemId}", UriKind.Relative),
                cancellationToken),
            cancellationToken);

    /// <summary>Reads a stock row's movements, newest first.</summary>
    /// <param name="stockItemId">The stock row.</param>
    public async Task<IReadOnlyList<JsonElement>> LedgerAsync(Guid stockItemId)
    {
        var page = await Rest.ReadAsync(
            await admin.GetAsync(
                new Uri($"/api/v1/admin/stock/{stockItemId}/ledger?size=200", UriKind.Relative),
                cancellationToken),
            cancellationToken);

        return [.. page.GetProperty("items").EnumerateArray()];
    }

    /// <summary>Schedules a count, freezing today's book figures onto the sheet.</summary>
    /// <param name="warehouseId">The location to count.</param>
    /// <param name="stockItemIds">Restrict the sheet to these rows, or empty for the whole location.</param>
    /// <param name="client">The client to act as. Defaults to the scenario's own.</param>
    public Task<HttpResponseMessage> StockTakeAsync(
        Guid warehouseId,
        IReadOnlyList<Guid>? stockItemIds = null,
        HttpClient? client = null)
        => (client ?? admin).PostAsJsonAsync(
            "/api/v1/admin/stock-takes",
            new
            {
                warehouseId,
                scheduledFor = (DateTimeOffset?)null,
                notes = "Counted by an integration test.",
                stockItemIds,
            },
            cancellationToken);

    /// <summary>Records what the counter found.</summary>
    /// <param name="stockTakeId">The sheet.</param>
    /// <param name="counts">One entry per counted row.</param>
    public Task<HttpResponseMessage> CountAsync(
        Guid stockTakeId,
        params (Guid StockItemId, int Counted)[] counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        return admin.PutAsJsonAsync(
            $"/api/v1/admin/stock-takes/{stockTakeId}/lines",
            new
            {
                lines = counts.Select(count => new
                {
                    stockItemId = count.StockItemId,
                    countedQuantity = count.Counted,
                    note = (string?)null,
                }).ToArray(),
            },
            cancellationToken);
    }

    /// <summary>Posts a sheet's variances to the ledger.</summary>
    /// <param name="stockTakeId">The sheet.</param>
    public Task<HttpResponseMessage> SubmitStockTakeAsync(Guid stockTakeId)
        => admin.PostAsJsonAsync(
            $"/api/v1/admin/stock-takes/{stockTakeId}/submit",
            new { },
            cancellationToken);

    /// <summary>
    /// A published offer with a location and an open stock row, and units on the shelf if asked for.
    /// </summary>
    /// <remarks>
    /// The whole chain, every link of it through the API: a product drafted, its variant activated,
    /// the product published, an offer opened against it, a location opened, a stock row opened for
    /// the pair, and the units adjusted in with a reason. That is what makes the ledger sums a test
    /// asserts on the product's arithmetic rather than the test's.
    /// </remarks>
    /// <param name="catalogue">The catalogue builder, for the product and the offer.</param>
    /// <param name="taxonomy">The vocabulary to describe the product with.</param>
    /// <param name="vendorId">The seller who offers it, or null for a platform-owned offer.</param>
    /// <param name="quantity">How many units to put on the shelf.</param>
    /// <param name="warehouseId">An existing location, or null to open one.</param>
    public async Task<StockedOffer> StockedAsync(
        CatalogScenario catalogue,
        CatalogTaxonomy taxonomy,
        Guid? vendorId,
        int quantity = 0,
        Guid? warehouseId = null)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var product = await catalogue.DraftAsync(taxonomy);

        await Rest.ReadAsync(await catalogue.ActivateVariantAsync(product.VariantId), cancellationToken);
        await catalogue.PublishAsync(product.Id);

        var listingId = await catalogue.OfferAsync(vendorId, product.VariantId, 899m);
        var location = warehouseId ?? await WarehouseAsync(vendorId);
        var stockItemId = await StockItemAsync(listingId, location);

        if (quantity > 0)
        {
            await Rest.ReadAsync(
                await AdjustAsync(stockItemId, quantity, "Adjustment", "Opening stock."),
                cancellationToken);
        }

        return new StockedOffer(
            vendorId,
            product.Id,
            product.VariantId,
            listingId,
            location,
            stockItemId,
            product.Sku);
    }

    /// <summary>A short suffix nothing else in the collection is using.</summary>
    private static string Suffix() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
}
