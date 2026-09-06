using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Inventory.Application.Stock;
using KlaraHome.Modules.Inventory.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Inventory.Endpoints;

/// <summary>Query-string filters for the stock listing.</summary>
/// <param name="WarehouseId">Restrict to one location.</param>
/// <param name="ListingId">Restrict to one offer.</param>
/// <param name="VendorId">Restrict to one seller.</param>
/// <param name="LowStock">Only rows at or below their reorder level.</param>
/// <param name="OutOfStock">Only rows with nothing available.</param>
/// <param name="Search">A fragment of the SKU.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record StockListFilter(
    Guid? WarehouseId,
    Guid? ListingId,
    Guid? VendorId,
    bool? LowStock,
    bool? OutOfStock,
    string? Search,
    string? Cursor,
    int? Size);

/// <summary>Query-string filters for a stock row's ledger.</summary>
/// <param name="From">Only movements on or after this instant.</param>
/// <param name="To">Only movements before this instant.</param>
/// <param name="Reason">Only movements with this reason.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record StockLedgerFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Reason,
    string? Cursor,
    int? Size);

/// <summary>The body that opens a stock row.</summary>
/// <param name="ListingId">The offer.</param>
/// <param name="WarehouseId">Where it will be held.</param>
internal sealed record OpenStockItemBody(Guid ListingId, Guid WarehouseId);

/// <summary>The body of a change to a stock row's policy.</summary>
/// <param name="ReorderLevel">The level at or below which to alert. Zero disables the alert.</param>
/// <param name="ReorderQuantity">How many the seller buys at a time.</param>
/// <param name="AllowBackorder">Whether to accept orders beyond what is on hand.</param>
/// <param name="AllowPreorder">Whether the offer may be sold before release.</param>
/// <param name="PreorderAvailableAt">When a pre-ordered unit is expected to ship.</param>
/// <param name="TrackingMode">How closely individual units are tracked.</param>
internal sealed record StockSettingsBody(
    int ReorderLevel,
    int ReorderQuantity,
    bool AllowBackorder,
    bool AllowPreorder,
    DateTimeOffset? PreorderAvailableAt,
    StockTrackingMode TrackingMode);

/// <summary>The body of a manual movement.</summary>
/// <param name="StockItemId">The stock row.</param>
/// <param name="Change">Signed units.</param>
/// <param name="Reason">Why. Only the reasons an operator may choose are accepted.</param>
/// <param name="Note">What they wrote.</param>
internal sealed record StockAdjustmentBody(
    Guid StockItemId,
    int Change,
    StockMovementReason Reason,
    string? Note);

/// <summary>The body of a transfer between locations.</summary>
/// <param name="ListingId">The offer.</param>
/// <param name="FromWarehouseId">Where the units leave.</param>
/// <param name="ToWarehouseId">Where they arrive.</param>
/// <param name="Quantity">How many.</param>
/// <param name="Note">Why.</param>
internal sealed record StockTransferBody(
    Guid ListingId,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    int Quantity,
    string? Note);

/// <summary>The body that records a lot.</summary>
/// <param name="BatchCode">The supplier's lot number.</param>
/// <param name="Quantity">How many units of it are held.</param>
/// <param name="ManufacturedOn">When it was made.</param>
/// <param name="ExpiresOn">When it expires.</param>
/// <param name="SupplierId">Who it came from.</param>
internal sealed record StockBatchBody(
    string BatchCode,
    int Quantity,
    DateOnly? ManufacturedOn,
    DateOnly? ExpiresOn,
    Guid? SupplierId);

/// <summary>The body that books individually identified units in.</summary>
/// <param name="SerialNumbers">The manufacturers' numbers.</param>
/// <param name="BatchId">The lot they came in.</param>
internal sealed record StockSerialsBody(IReadOnlyList<string> SerialNumbers, Guid? BatchId);

/// <summary>
/// The stock surface (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// Reading is one permission and moving is another, deliberately: a manual correction is how stock
/// appears from nowhere, and it is the action in this module a supervisor signs off. The ledger, the
/// holds, the lots and the serials all hang off a stock row and are gated by the right to read it.
/// </remarks>
internal static class AdminStockEndpoints
{
    /// <summary>Maps the stock surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminStockEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var stock = admin.MapGroup("/stock").WithTags("Inventory");

        stock.MapGet("/", async (
                [AsParameters] StockListFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListStockQuery(
                    filter.WarehouseId,
                    filter.ListingId,
                    filter.VendorId,
                    filter.LowStock,
                    filter.OutOfStock,
                    filter.Search,
                    filter.Cursor,
                    filter.Size);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockList")
            .WithSummary("Lists stock rows. A vendor caller sees only their own.")
            .RequirePermission(InventoryPermissions.StockRead)
            .Produces<PagedResult<StockItemResponse>>();

        stock.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetStockItemQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockGet")
            .WithSummary("Reads one stock row, with its on-hand, reserved and available counts.")
            .RequirePermission(InventoryPermissions.StockRead)
            .Produces<StockItemResponse>();

        stock.MapPost("/", async (OpenStockItemBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new OpenStockItemCommand(body.ListingId, body.WarehouseId);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    item => Results.Created($"{context.Request.Path}/{item.Id}", item),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminStockOpen")
            .WithSummary("Opens a stock row for an offer at a location, at zero. One row per pair.")
            .RequirePermission(InventoryPermissions.StockAdjust)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<StockItemResponse>(StatusCodes.Status201Created);

        stock.MapPut("/{id:guid}/settings", async (
                Guid id,
                StockSettingsBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new ConfigureStockItemCommand(
                    id,
                    body.ReorderLevel,
                    body.ReorderQuantity,
                    body.AllowBackorder,
                    body.AllowPreorder,
                    body.PreorderAvailableAt,
                    body.TrackingMode);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockConfigure")
            .WithSummary("Sets the reorder level, the backorder and pre-order flags, and the tracking mode.")
            .RequirePermission(InventoryPermissions.StockAdjust)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<StockItemResponse>();

        stock.MapPost("/adjustments", async (
                StockAdjustmentBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new AdjustStockCommand(body.StockItemId, body.Change, body.Reason, body.Note);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockAdjust")
            .WithSummary("Moves stock by hand with a reason. Refused if it would take the location below empty.")
            .RequirePermission(InventoryPermissions.StockAdjust)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<StockItemResponse>();

        stock.MapPost("/transfers", async (
                StockTransferBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new TransferStockCommand(
                    body.ListingId,
                    body.FromWarehouseId,
                    body.ToWarehouseId,
                    body.Quantity,
                    body.Note);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockTransfer")
            .WithSummary("Moves units between two locations as one movement, written as two ledger entries.")
            .RequirePermission(InventoryPermissions.StockAdjust)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<IReadOnlyList<StockItemResponse>>();

        stock.MapGet("/{id:guid}/ledger", async (
                Guid id,
                [AsParameters] StockLedgerFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListStockLedgerQuery(
                    id,
                    filter.From,
                    filter.To,
                    filter.Reason,
                    filter.Cursor,
                    filter.Size);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockLedger")
            .WithSummary("Every movement of one stock row, newest first. Append-only and complete.")
            .RequirePermission(InventoryPermissions.StockRead)
            .Produces<PagedResult<StockLedgerEntryResponse>>();

        stock.MapGet("/{id:guid}/reservations", async (
                Guid id,
                string? status,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListStockReservationsQuery(id, status, cursor, size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockReservations")
            .WithSummary("The holds against one stock row: who has it, how much, and until when.")
            .RequirePermission(InventoryPermissions.StockRead)
            .Produces<PagedResult<StockReservationResponse>>();

        MapTracking(stock);

        return admin;
    }

    /// <summary>Maps the batch and serial endpoints, which only a tracked item populates.</summary>
    private static void MapTracking(IEndpointRouteBuilder stock)
    {
        stock.MapGet("/{id:guid}/batches", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListStockBatchesQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockBatches")
            .WithSummary("The lots held against one stock row, soonest to expire first.")
            .RequirePermission(InventoryPermissions.StockRead)
            .Produces<IReadOnlyList<StockBatchResponse>>();

        stock.MapPost("/{id:guid}/batches", async (
                Guid id,
                StockBatchBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new RecordStockBatchCommand(
                    id,
                    body.BatchCode,
                    body.Quantity,
                    body.ManufacturedOn,
                    body.ExpiresOn,
                    body.SupplierId);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockBatchRecord")
            .WithSummary("Records a lot. A second delivery of the same lot number adds to it.")
            .RequirePermission(InventoryPermissions.StockAdjust)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<StockBatchResponse>();

        stock.MapGet("/{id:guid}/serials", async (
                Guid id,
                string? status,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListStockSerialsQuery(id, status), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockSerials")
            .WithSummary("The individually identified units held against one stock row.")
            .RequirePermission(InventoryPermissions.StockRead)
            .Produces<IReadOnlyList<StockSerialResponse>>();

        stock.MapPost("/{id:guid}/serials", async (
                Guid id,
                StockSerialsBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new RecordStockSerialsCommand(id, body.SerialNumbers, body.BatchId);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockSerialsRecord")
            .WithSummary("Books units in by serial number. Numbers already known are skipped, not refused.")
            .RequirePermission(InventoryPermissions.StockAdjust)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<IReadOnlyList<StockSerialResponse>>();
    }
}
