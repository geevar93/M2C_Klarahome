using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Inventory.Application.Purchasing;
using KlaraHome.Modules.Inventory.Application.Warehouses;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Inventory.Endpoints;

/// <summary>Query-string filters for the supplier listing.</summary>
/// <param name="ActiveOnly">Hide the ones no longer traded with.</param>
/// <param name="Search">A fragment of the code or the name.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record SupplierListFilter(bool? ActiveOnly, string? Search, string? Cursor, int? Size);

/// <summary>The body of a new supplier.</summary>
/// <param name="VendorId">Whose list. Ignored for a vendor caller.</param>
/// <param name="Code">The short code a buyer quotes.</param>
/// <param name="Name">Their trading name.</param>
/// <param name="ContactName">Who to speak to.</param>
/// <param name="Email">Where the purchase order is emailed.</param>
/// <param name="Phone">The number to ring.</param>
/// <param name="Gstin">Their GST registration.</param>
/// <param name="Address">Where they are.</param>
/// <param name="PaymentTermsDays">How many days their invoice falls due in.</param>
internal sealed record CreateSupplierBody(
    Guid? VendorId,
    string Code,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Gstin,
    AddressPayload? Address,
    int PaymentTermsDays);

/// <summary>The body of a change to a supplier.</summary>
/// <param name="Name">Their trading name.</param>
/// <param name="ContactName">Who to speak to.</param>
/// <param name="Email">Where the purchase order is emailed.</param>
/// <param name="Phone">The number to ring.</param>
/// <param name="Gstin">Their GST registration.</param>
/// <param name="Address">Where they are.</param>
/// <param name="PaymentTermsDays">How many days their invoice falls due in.</param>
/// <param name="IsActive">Whether orders may still be raised on them.</param>
internal sealed record UpdateSupplierBody(
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Gstin,
    AddressPayload? Address,
    int PaymentTermsDays,
    bool IsActive);

/// <summary>Query-string filters for the purchase-order listing.</summary>
/// <param name="Status">Restrict to one status.</param>
/// <param name="SupplierId">Restrict to one supplier.</param>
/// <param name="WarehouseId">Restrict to one destination.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record PurchaseOrderListFilter(
    string? Status,
    Guid? SupplierId,
    Guid? WarehouseId,
    string? Cursor,
    int? Size);

/// <summary>The body of a new purchase order.</summary>
/// <param name="SupplierId">Who it is placed on.</param>
/// <param name="WarehouseId">Where the goods go.</param>
/// <param name="ExpectedAt">When the supplier said it would arrive.</param>
/// <param name="Notes">Anything the buyer wrote.</param>
/// <param name="Lines">The lines.</param>
internal sealed record CreatePurchaseOrderBody(
    Guid SupplierId,
    Guid WarehouseId,
    DateTimeOffset? ExpectedAt,
    string? Notes,
    IReadOnlyList<PurchaseOrderLinePayload> Lines);

/// <summary>The body of a change to a draft purchase order.</summary>
/// <param name="ExpectedAt">When the supplier said it would arrive.</param>
/// <param name="Notes">Anything the buyer wrote.</param>
/// <param name="Lines">The lines.</param>
internal sealed record UpdatePurchaseOrderBody(
    DateTimeOffset? ExpectedAt,
    string? Notes,
    IReadOnlyList<PurchaseOrderLinePayload> Lines);

/// <summary>The body that books goods in.</summary>
/// <param name="Notes">Anything the receiver wrote.</param>
/// <param name="Lines">What arrived.</param>
internal sealed record ReceivePurchaseOrderBody(
    string? Notes,
    IReadOnlyList<GoodsReceiptLinePayload> Lines);

/// <summary>
/// The purchasing surface: suppliers, orders and receipts (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// One permission covers all three, because they are one job: the person who keeps the supplier
/// list is the person who raises the order and the person who signs for the pallet. Splitting them
/// would produce three roles nobody has.
/// </remarks>
internal static class AdminPurchasingEndpoints
{
    /// <summary>Maps the purchasing surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminPurchasingEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        MapSuppliers(admin.MapGroup("/suppliers").WithTags("Inventory"));
        MapPurchaseOrders(admin.MapGroup("/purchase-orders").WithTags("Inventory"));
        MapGoodsReceipts(admin.MapGroup("/goods-receipts").WithTags("Inventory"));

        return admin;
    }

    private static void MapSuppliers(IEndpointRouteBuilder suppliers)
    {
        suppliers.MapGet("/", async (
                [AsParameters] SupplierListFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListSuppliersQuery(
                    filter.ActiveOnly,
                    filter.Search,
                    filter.Cursor,
                    filter.Size);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSuppliersList")
            .WithSummary("Lists suppliers. A vendor caller sees their own and the platform's.")
            .RequirePermission(InventoryPermissions.PurchasingManage)
            .Produces<PagedResult<SupplierResponse>>();

        suppliers.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetSupplierQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSupplierGet")
            .WithSummary("Reads one supplier.")
            .RequirePermission(InventoryPermissions.PurchasingManage)
            .Produces<SupplierResponse>();

        suppliers.MapPost("/", async (CreateSupplierBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateSupplierCommand(
                    body.VendorId,
                    body.Code,
                    body.Name,
                    body.ContactName,
                    body.Email,
                    body.Phone,
                    body.Gstin,
                    body.Address,
                    body.PaymentTermsDays);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    supplier => Results.Created($"{context.Request.Path}/{supplier.Id}", supplier),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminSupplierCreate")
            .WithSummary("Adds a supplier. The code is unique across the store.")
            .RequirePermission(InventoryPermissions.PurchasingManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<SupplierResponse>(StatusCodes.Status201Created);

        suppliers.MapPut("/{id:guid}", async (
                Guid id,
                UpdateSupplierBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateSupplierCommand(
                    id,
                    body.Name,
                    body.ContactName,
                    body.Email,
                    body.Phone,
                    body.Gstin,
                    body.Address,
                    body.PaymentTermsDays,
                    body.IsActive);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSupplierUpdate")
            .WithSummary("Restates a supplier, and opens or closes them to new orders.")
            .RequirePermission(InventoryPermissions.PurchasingManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<SupplierResponse>();
    }

    private static void MapPurchaseOrders(IEndpointRouteBuilder orders)
    {
        orders.MapGet("/", async (
                [AsParameters] PurchaseOrderListFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListPurchaseOrdersQuery(
                    filter.Status,
                    filter.SupplierId,
                    filter.WarehouseId,
                    filter.Cursor,
                    filter.Size);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPurchaseOrdersList")
            .WithSummary("Lists purchase orders without their lines, newest first.")
            .RequirePermission(InventoryPermissions.PurchasingManage)
            .Produces<PagedResult<PurchaseOrderResponse>>();

        orders.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetPurchaseOrderQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPurchaseOrderGet")
            .WithSummary("Reads one purchase order, lines and all.")
            .RequirePermission(InventoryPermissions.PurchasingManage)
            .Produces<PurchaseOrderResponse>();

        orders.MapPost("/", async (
                CreatePurchaseOrderBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new CreatePurchaseOrderCommand(
                    body.SupplierId,
                    body.WarehouseId,
                    body.ExpectedAt,
                    body.Notes,
                    body.Lines);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    order => Results.Created($"{context.Request.Path}/{order.Id}", order),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminPurchaseOrderCreate")
            .WithSummary("Raises a draft purchase order. Nothing moves until goods are received.")
            .RequirePermission(InventoryPermissions.PurchasingManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PurchaseOrderResponse>(StatusCodes.Status201Created);

        orders.MapPut("/{id:guid}", async (
                Guid id,
                UpdatePurchaseOrderBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdatePurchaseOrderCommand(id, body.ExpectedAt, body.Notes, body.Lines);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPurchaseOrderUpdate")
            .WithSummary("Rewrites a draft. Refused once the order has been sent to the supplier.")
            .RequirePermission(InventoryPermissions.PurchasingManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PurchaseOrderResponse>();

        orders.MapPost("/{id:guid}/submit", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SubmitPurchaseOrderCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPurchaseOrderSubmit")
            .WithSummary("Sends the order to its supplier. The lines are frozen from then on.")
            .RequirePermission(InventoryPermissions.PurchasingManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PurchaseOrderResponse>();

        orders.MapPost("/{id:guid}/cancel", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new CancelPurchaseOrderCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminPurchaseOrderCancel")
            .WithSummary("Calls the order off. Refused once any of it has arrived.")
            .RequirePermission(InventoryPermissions.PurchasingManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PurchaseOrderResponse>();

        orders.MapPost("/{id:guid}/receive", async (
                Guid id,
                ReceivePurchaseOrderBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new ReceivePurchaseOrderCommand(id, body.Notes, body.Lines);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    receipt => Results.Created($"/admin/goods-receipts/{receipt.Id}", receipt),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminPurchaseOrderReceive")
            .WithSummary("Books goods in against the order and returns the GRN. This is what moves stock.")
            .RequirePermission(InventoryPermissions.PurchasingManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<GoodsReceiptResponse>(StatusCodes.Status201Created);
    }

    private static void MapGoodsReceipts(IEndpointRouteBuilder receipts)
    {
        receipts.MapGet("/", async (
                Guid? purchaseOrderId,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListGoodsReceiptsQuery(purchaseOrderId, cursor, size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGoodsReceiptsList")
            .WithSummary("Lists goods receipts, newest first.")
            .RequirePermission(InventoryPermissions.PurchasingManage)
            .Produces<PagedResult<GoodsReceiptResponse>>();

        receipts.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetGoodsReceiptQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGoodsReceiptGet")
            .WithSummary("Reads one goods receipt: what was accepted, what was refused, and why.")
            .RequirePermission(InventoryPermissions.PurchasingManage)
            .Produces<GoodsReceiptResponse>();
    }
}
