using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Inventory.Application.StockTakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Inventory.Endpoints;

/// <summary>Query-string filters for the stock-take listing.</summary>
/// <param name="Status">Restrict to one status.</param>
/// <param name="WarehouseId">Restrict to one location.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record StockTakeListFilter(
    string? Status,
    Guid? WarehouseId,
    string? Cursor,
    int? Size);

/// <summary>The body that schedules a count.</summary>
/// <param name="WarehouseId">The location to count.</param>
/// <param name="ScheduledFor">When the count is to be taken.</param>
/// <param name="Notes">Anything the planner wrote.</param>
/// <param name="StockItemIds">Restrict the sheet to these rows, or omit for the whole location.</param>
internal sealed record CreateStockTakeBody(
    Guid WarehouseId,
    DateTimeOffset? ScheduledFor,
    string? Notes,
    IReadOnlyList<Guid>? StockItemIds);

/// <summary>The body that records what the counter found.</summary>
/// <param name="Lines">The counts.</param>
internal sealed record StockTakeCountsBody(IReadOnlyList<StockTakeCountPayload> Lines);

/// <summary>
/// The stock-take surface (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// Its own permission, separate from adjustment, because a stock take is how a business finds out
/// what it actually has: the count is entered by whoever walked the aisles, and submitting it posts
/// corrections nobody typed a number for.
/// </remarks>
internal static class AdminStockTakeEndpoints
{
    /// <summary>Maps the stock-take surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminStockTakeEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var takes = admin.MapGroup("/stock-takes").WithTags("Inventory");

        takes.MapGet("/", async (
                [AsParameters] StockTakeListFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListStockTakesQuery(
                    filter.Status,
                    filter.WarehouseId,
                    filter.Cursor,
                    filter.Size);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockTakesList")
            .WithSummary("Lists stock takes without their sheets, newest first.")
            .RequirePermission(InventoryPermissions.StockTakeManage)
            .Produces<PagedResult<StockTakeResponse>>();

        takes.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetStockTakeQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockTakeGet")
            .WithSummary("Reads one stock take with its sheet: expected, counted and the variance.")
            .RequirePermission(InventoryPermissions.StockTakeManage)
            .Produces<StockTakeResponse>();

        takes.MapPost("/", async (CreateStockTakeBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateStockTakeCommand(
                    body.WarehouseId,
                    body.ScheduledFor,
                    body.Notes,
                    body.StockItemIds ?? []);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    take => Results.Created($"{context.Request.Path}/{take.Id}", take),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminStockTakeCreate")
            .WithSummary("Opens a count sheet with today's book figures frozen onto it.")
            .RequirePermission(InventoryPermissions.StockTakeManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<StockTakeResponse>(StatusCodes.Status201Created);

        takes.MapPut("/{id:guid}/lines", async (
                Guid id,
                StockTakeCountsBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new RecordStockTakeCountsCommand(id, body.Lines);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockTakeCount")
            .WithSummary("Records counts. Partial submissions are normal; uncounted rows stay uncounted.")
            .RequirePermission(InventoryPermissions.StockTakeManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<StockTakeResponse>();

        takes.MapPost("/{id:guid}/submit", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SubmitStockTakeCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockTakeSubmit")
            .WithSummary("Posts one correction per non-zero variance. Uncounted rows are left alone.")
            .RequirePermission(InventoryPermissions.StockTakeManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<StockTakeResponse>();

        takes.MapPost("/{id:guid}/cancel", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new CancelStockTakeCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminStockTakeCancel")
            .WithSummary("Abandons the count without posting anything.")
            .RequirePermission(InventoryPermissions.StockTakeManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<StockTakeResponse>();

        return admin;
    }
}
