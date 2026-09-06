using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Orders.Application.Invoices;
using KlaraHome.Modules.Orders.Application.Orders;
using KlaraHome.Modules.Orders.Infrastructure.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Orders.Endpoints;

/// <summary>The body of a lifecycle transition.</summary>
/// <param name="Status">Where to move the sub-order.</param>
/// <param name="Reason">Why, when a reason is called for.</param>
internal sealed record TransitionBody(string Status, string? Reason);

/// <summary>The body of an internal note.</summary>
/// <param name="Message">What to write down.</param>
/// <param name="IsCustomerVisible">Whether the shopper sees it. Off unless asked for.</param>
internal sealed record OrderNoteBody(string Message, bool IsCustomerVisible);

/// <summary>
/// The fulfilment surface, for platform staff and for sellers (docs/04-api-specification.md §2).
/// </summary>
/// <remarks>
/// <para>
/// One set of routes serving two audiences, separated by the caller's vendor scope rather than by a
/// vendor id in a path. A seller addressing a sub-order that is not theirs does not get a 403 — the
/// vendor query filter means the row does not exist for them, and they get the same 404 an invented
/// id gets.
/// </para>
/// <para>
/// The transition route takes a target state rather than offering a verb per edge. The machine is
/// the authority on what may happen next and it already answers that question — a
/// <c>POST /confirm</c>, <c>/pack</c>, <c>/ship</c> surface would be the same table written a second
/// time in the routing, and the two would drift. Each sub-order response carries
/// <c>nextStatuses</c> for the asking caller, so an admin screen offers exactly the buttons that
/// will work.
/// </para>
/// </remarks>
internal static class AdminOrderEndpoints
{
    /// <summary>Maps the fulfilment surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminOrderEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var orders = admin.MapGroup("/orders").WithTags("Orders");

        orders.MapGet("/", async (
                string? status,
                string? paymentStatus,
                Guid? customerId,
                string? number,
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListOrdersQuery(status, paymentStatus, customerId, number, from, to, cursor, size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListOrders")
            .WithSummary("Orders, newest first. A vendor caller sees only orders they have a part in.")
            .RequirePermission(OrdersPermissions.OrderRead)
            .Produces<PagedResult<OrderSummaryResponse>>();

        orders.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetOrderQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminGetOrder")
            .WithSummary("One order in full: sellers, lines, invoices and the whole timeline.")
            .RequirePermission(OrdersPermissions.OrderRead)
            .Produces<OrderResponse>();

        orders.MapGet("/{id:guid}/invoices", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListOrderInvoicesQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListOrderInvoices")
            .WithSummary("The tax invoices raised against the order, one per seller.")
            .RequirePermission(OrdersPermissions.OrderRead)
            .Produces<IReadOnlyList<InvoiceResponse>>();

        orders.MapPost("/{id:guid}/notes", async (
                Guid id,
                OrderNoteBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new AddOrderNoteCommand(id, body.Message, body.IsCustomerVisible);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminAddOrderNote")
            .WithSummary("Appends a note to the timeline. Internal unless the body says otherwise.")
            .RequirePermission(OrdersPermissions.OrderTransition)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<OrderResponse>();

        var subOrders = admin.MapGroup("/sub-orders").WithTags("Orders");

        subOrders.MapGet("/", async (
                string? status,
                Guid? vendorId,
                bool? overdueOnly,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListSubOrdersQuery(status, vendorId, overdueOnly, cursor, size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminListSubOrders")
            .WithSummary("The fulfilment worklist. A vendor caller sees only their own.")
            .RequirePermission(OrdersPermissions.OrderRead)
            .Produces<PagedResult<SubOrderResponse>>();

        subOrders.MapPost("/{id:guid}/transition", async (
                Guid id,
                TransitionBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new TransitionSubOrderCommand(id, body.Status, body.Reason);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminTransitionSubOrder")
            .WithSummary("Moves a sub-order to the named state, if the machine allows it for this caller.")
            .RequirePermission(OrdersPermissions.OrderTransition)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<SubOrderResponse>();

        subOrders.MapPost("/{id:guid}/cancel", async (
                Guid id,
                CancelOrderBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new CancelSubOrderCommand(
                    id,
                    body?.Reason,
                    body?.Lines is null
                        ? null
                        : [.. body.Lines.Select(line => new CancellationLine(line.OrderLineId, line.Quantity))]);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCancelSubOrder")
            .WithSummary("Cancels a sub-order, or the units the body names. A reason is required after dispatch.")
            .RequirePermission(OrdersPermissions.OrderCancel)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<SubOrderResponse>();

        subOrders.MapPost("/{id:guid}/invoice", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new IssueInvoiceCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminIssueInvoice")
            .WithSummary("Raises the tax invoice by hand, for the parcel that was dispatched without one.")
            .RequirePermission(OrdersPermissions.InvoiceManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<InvoiceResponse>();

        admin.MapGet("/invoices/{id:guid}/download", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetInvoiceDownloadQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithTags("Orders")
            .WithName("adminDownloadInvoice")
            .WithSummary("A short-lived link to the invoice PDF. Minting the link is the grant.")
            .RequirePermission(OrdersPermissions.OrderRead)
            .Produces<InvoiceDownloadResponse>();

        return admin;
    }
}
