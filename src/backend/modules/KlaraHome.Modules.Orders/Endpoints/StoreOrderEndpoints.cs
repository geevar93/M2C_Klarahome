using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Orders.Application;
using KlaraHome.Modules.Orders.Application.Invoices;
using KlaraHome.Modules.Orders.Application.Orders;
using KlaraHome.Modules.Orders.Infrastructure.Lifecycle;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Orders.Endpoints;

/// <summary>One line of a partial cancellation, as the API states it.</summary>
/// <param name="OrderLineId">The line.</param>
/// <param name="Quantity">How many units to cancel.</param>
internal sealed record CancelLineBody(Guid OrderLineId, int Quantity);

/// <summary>The body of a cancellation.</summary>
/// <param name="Reason">Why, in the shopper's own words.</param>
/// <param name="Lines">Which units, or null for all of them.</param>
internal sealed record CancelOrderBody(string? Reason, IReadOnlyList<CancelLineBody>? Lines);

/// <summary>
/// What a shopper can see and do about their own orders (docs/04-api-specification.md §3.1).
/// </summary>
/// <remarks>
/// <para>
/// Every route requires an account and every handler resolves an order by <em>(order, customer)</em>.
/// An id belonging to somebody else does not resolve, and the answer is the same 404 a made-up id
/// gets — telling a caller that another shopper's order id is real is a disclosure, however little
/// else it reveals.
/// </para>
/// <para>
/// There is no route here that edits an order. A shopper may look at one, follow it, cancel what is
/// still cancellable and download their invoice; everything else about an order is something a
/// seller or an operator does, and no support question is worth an endpoint that lets a customer
/// change what they are being charged.
/// </para>
/// </remarks>
internal static class StoreOrderEndpoints
{
    /// <summary>Maps the order surface beneath <c>/store/orders</c>.</summary>
    /// <param name="store">The <c>/store</c> group.</param>
    public static IEndpointRouteBuilder MapStoreOrderEndpoints(this IEndpointRouteBuilder store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var group = store
            .MapGroup("/orders")
            .WithTags("Orders")
            .RequireAuthorization();

        group.MapGet("/", async (
                string? status,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListMyOrdersQuery(status, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeListOrders")
            .WithSummary("The caller's own orders, newest first.")
            .Produces<PagedResult<OrderSummaryResponse>>();

        // `{reference}` rather than `{id:guid}`: the storefront's URLs carry the order NUMBER -
        // `/checkout/confirmation/KH-2609-000002`, `/account/orders/KH-2609-000002` - because that
        // is what a shopper bookmarks, screenshots and reads out over the phone, and the same
        // string is what it asks the API for. A guid-only route answered 404 to every one of them
        // before any handler ran. Both forms are accepted; ownership is still checked by the handler.
        group.MapGet("/{reference}", async (
                string reference,
                OrdersDbContext orders,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var id = await ResolveAsync(orders, reference, context.RequestAborted).ConfigureAwait(false);
                if (id is null)
                {
                    return Result.Failure<OrderResponse>(NotFound()).ToOk(context);
                }

                var result = await dispatcher
                    .QueryAsync(new GetMyOrderQuery(id.Value), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetOrder")
            .WithSummary("One of the caller's own orders in full, with a section per seller.")
            .Produces<OrderResponse>();

        group.MapGet("/{reference}/timeline", async (
                string reference,
                OrdersDbContext orders,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var id = await ResolveAsync(orders, reference, context.RequestAborted).ConfigureAwait(false);
                if (id is null)
                {
                    return Result.Failure<IReadOnlyList<OrderEventResponse>>(NotFound()).ToOk(context);
                }

                var result = await dispatcher
                    .QueryAsync(new GetMyOrderTimelineQuery(id.Value), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeGetOrderTimeline")
            .WithSummary("What has happened to the order, oldest first. Internal entries are not included.")
            .Produces<IReadOnlyList<OrderEventResponse>>();

        group.MapPost("/{reference}/cancel", async (
                string reference,
                CancelOrderBody? body,
                OrdersDbContext orders,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var id = await ResolveAsync(orders, reference, context.RequestAborted).ConfigureAwait(false);
                if (id is null)
                {
                    return Result.Failure<OrderResponse>(NotFound()).ToOk(context);
                }

                var result = await dispatcher
                    .SendAsync(new CancelMyOrderCommand(id.Value, body?.Reason), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeCancelOrder")
            .WithSummary("Cancels every part of the order that may still be cancelled, and says what could not.")
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<OrderResponse>();

        group.MapGet("/{reference}/invoices", async (
                string reference,
                OrdersDbContext orders,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var id = await ResolveAsync(orders, reference, context.RequestAborted).ConfigureAwait(false);
                if (id is null)
                {
                    return Result.Failure<IReadOnlyList<InvoiceResponse>>(NotFound()).ToOk(context);
                }

                var result = await dispatcher
                    .QueryAsync(new ListOrderInvoicesQuery(id.Value), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeListOrderInvoices")
            .WithSummary("The tax invoices raised against the order, one per seller.")
            .Produces<IReadOnlyList<InvoiceResponse>>();

        store.MapPost("/sub-orders/{id:guid}/cancel", async (
                Guid id,
                CancelOrderBody? body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new CancelMySubOrderCommand(
                    id,
                    body?.Reason,
                    body?.Lines is null
                        ? null
                        : [.. body.Lines.Select(line => new CancellationLine(line.OrderLineId, line.Quantity))]);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithTags("Orders")
            .RequireAuthorization()
            .WithName("storeCancelSubOrder")
            .WithSummary("Cancels one seller's part, or the units of it the body names.")
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<OrderResponse>();

        store.MapGet("/invoices/{id:guid}/download", async (
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
            .RequireAuthorization()
            .WithName("storeDownloadInvoice")
            .WithSummary("A short-lived link to the invoice PDF. Minting the link is the grant.")
            .Produces<InvoiceDownloadResponse>();

        return store;
    }

    /// <summary>
    /// Turns a route reference into an order id: a guid as it is, anything else as an order
    /// number. Only the id is resolved here; whether the caller may see the order is the
    /// handler's decision, so an order number that is not theirs answers exactly what a guessed
    /// guid would.
    /// </summary>
    private static async Task<Guid?> ResolveAsync(
        OrdersDbContext orders,
        string reference,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(reference, out var id))
        {
            return id;
        }

        var number = reference.Trim().ToUpperInvariant();
        if (number.Length == 0)
        {
            return null;
        }

        var found = await orders.Orders
            .AsNoTracking()
            .Where(order => order.OrderNumber == number)
            .Select(order => (Guid?)order.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return found;
    }

    private static Error NotFound() => OrdersErrors.NotFound("order");
}
