using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Inventory.Application.Warehouses;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Inventory.Endpoints;

/// <summary>Query-string filters for the location listing.</summary>
/// <param name="VendorId">Restrict to one seller.</param>
/// <param name="ActiveOnly">Hide closed locations.</param>
/// <param name="Search">A fragment of the code or the name.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record WarehouseListFilter(
    Guid? VendorId,
    bool? ActiveOnly,
    string? Search,
    string? Cursor,
    int? Size);

/// <summary>The body of a new location.</summary>
/// <param name="VendorId">The seller. Ignored for a vendor caller, who opens their own.</param>
/// <param name="Code">The short code an operator quotes.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="Address">Where it is.</param>
/// <param name="Priority">Allocation order. Lower wins.</param>
internal sealed record CreateWarehouseBody(
    Guid? VendorId,
    string Code,
    string Name,
    string Pincode,
    AddressPayload? Address,
    int Priority);

/// <summary>The body of a change to a location.</summary>
/// <param name="Name">What it is called.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="Address">Where it is.</param>
/// <param name="Priority">Allocation order.</param>
/// <param name="IsActive">Whether stock may still move through it.</param>
internal sealed record UpdateWarehouseBody(
    string Name,
    string Pincode,
    AddressPayload? Address,
    int Priority,
    bool IsActive);

/// <summary>
/// The stock locations surface (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// Vendor-scoped by the caller's token: a seller sees their own locations and the platform's, and
/// may write only to their own. Platform staff name the seller in the body when they open one on
/// somebody's behalf.
/// </remarks>
internal static class AdminWarehouseEndpoints
{
    /// <summary>Maps the stock locations surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminWarehouseEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var warehouses = admin.MapGroup("/warehouses").WithTags("Inventory");

        warehouses.MapGet("/", async (
                [AsParameters] WarehouseListFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListWarehousesQuery(
                    filter.VendorId,
                    filter.ActiveOnly,
                    filter.Search,
                    filter.Cursor,
                    filter.Size);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminWarehousesList")
            .WithSummary("Lists stock locations. A vendor caller sees their own and the platform's.")
            .RequirePermission(InventoryPermissions.StockRead)
            .Produces<PagedResult<WarehouseResponse>>();

        warehouses.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetWarehouseQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminWarehouseGet")
            .WithSummary("Reads one location. Answers 404 for a location outside the caller's scope.")
            .RequirePermission(InventoryPermissions.StockRead)
            .Produces<WarehouseResponse>();

        warehouses.MapPost("/", async (
                CreateWarehouseBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new CreateWarehouseCommand(
                    body.VendorId,
                    body.Code,
                    body.Name,
                    body.Pincode,
                    body.Address,
                    body.Priority);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    warehouse => Results.Created($"{context.Request.Path}/{warehouse.Id}", warehouse),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminWarehouseCreate")
            .WithSummary("Opens a stock location. The code is unique across the store.")
            .RequirePermission(InventoryPermissions.WarehouseManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<WarehouseResponse>(StatusCodes.Status201Created);

        warehouses.MapPut("/{id:guid}", async (
                Guid id,
                UpdateWarehouseBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateWarehouseCommand(
                    id,
                    body.Name,
                    body.Pincode,
                    body.Address,
                    body.Priority,
                    body.IsActive);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminWarehouseUpdate")
            .WithSummary("Renames a location, restates where it is, and opens or closes it.")
            .RequirePermission(InventoryPermissions.WarehouseManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<WarehouseResponse>();

        warehouses.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteWarehouseCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminWarehouseDelete")
            .WithSummary("Removes an empty location. One holding stock is closed instead, not removed.")
            .RequirePermission(InventoryPermissions.WarehouseManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);

        return admin;
    }
}
