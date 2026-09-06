using FluentValidation;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure;
using KlaraHome.Modules.Orders.Infrastructure.Lifecycle;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Orders.Application.Orders;

/// <summary>Lists the caller's own orders, newest first.</summary>
/// <param name="Status">Restrict to one derived order status.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListMyOrdersQuery(string? Status, string? Cursor, int? Size)
    : IQuery<PagedResult<OrderSummaryResponse>>;

/// <summary>Reads one of the caller's own orders in full.</summary>
/// <param name="OrderId">The order.</param>
internal sealed record GetMyOrderQuery(Guid OrderId) : IQuery<OrderResponse>;

/// <summary>Reads the shopper-visible timeline of one of the caller's own orders.</summary>
/// <param name="OrderId">The order.</param>
internal sealed record GetMyOrderTimelineQuery(Guid OrderId) : IQuery<IReadOnlyList<OrderEventResponse>>;

/// <summary>Cancels every part of one of the caller's own orders that may still be cancelled.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="Reason">Why, in the shopper's own words.</param>
internal sealed record CancelMyOrderCommand(Guid OrderId, string? Reason) : ICommand<OrderResponse>;

/// <summary>Cancels one seller's part, or some units of it.</summary>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="Reason">Why.</param>
/// <param name="Lines">Which units, or null for all of them.</param>
internal sealed record CancelMySubOrderCommand(
    Guid SubOrderId,
    string? Reason,
    IReadOnlyList<CancellationLine>? Lines) : ICommand<OrderResponse>;

/// <summary>Rejects a cancellation that could never be attempted.</summary>
internal sealed class CancelMySubOrderValidator : AbstractValidator<CancelMySubOrderCommand>
{
    public CancelMySubOrderValidator()
    {
        RuleFor(command => command.SubOrderId).NotEmpty();
        RuleFor(command => command.Reason).MaximumLength(500);
        RuleForEach(command => command.Lines).ChildRules(line =>
        {
            line.RuleFor(candidate => candidate.OrderLineId).NotEmpty();
            line.RuleFor(candidate => candidate.Quantity).GreaterThan(0);
        });
    }
}

/// <summary>
/// Lists the caller's own orders.
/// </summary>
/// <remarks>
/// Scoped by the token and never by a parameter. There is no route on the storefront that takes a
/// customer id, and that is not an oversight — an id in a query string is an id a caller can change.
/// </remarks>
/// <param name="context">The Ordering data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListMyOrdersQueryHandler(
    OrdersDbContext context,
    OrdersScope scope,
    IOptions<OrdersOptions> options)
    : IQueryHandler<ListMyOrdersQuery, PagedResult<OrderSummaryResponse>>
{
    public async Task<Result<PagedResult<OrderSummaryResponse>>> HandleAsync(
        ListMyOrdersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (scope.CustomerId is not { } customerId)
        {
            return OrdersErrors.NotFound("order");
        }

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);

        var rows = context.Orders
            .AsNoTracking()
            .Include(order => order.SubOrders)
            .ThenInclude(subOrder => subOrder.Lines)
            .Where(order => order.CustomerId == customerId);

        if (Enum.TryParse<OrderStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(order => order.Status == status);
        }

        // Keyset on the id, which is a UUIDv7 and therefore already in placement order. Paging on
        // placed_at would need a tie-break the moment two orders share a millisecond.
        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(order => order.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(order => order.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(OrderProjection.ToSummary).ToArray();

        // The cursor is the last row of this page, so an empty page has none — the guard matters
        // because "no more orders" and "no orders at all" reach here by the same route.
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<OrderSummaryResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads one of the caller's own orders.</summary>
/// <param name="context">The Ordering data context.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class GetMyOrderQueryHandler(OrdersDbContext context, OrdersScope scope)
    : IQueryHandler<GetMyOrderQuery, OrderResponse>
{
    public async Task<Result<OrderResponse>> HandleAsync(
        GetMyOrderQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var loaded = await StoreOrders
            .LoadOwnAsync(context, scope, query.OrderId, tracked: false, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var invoices = await OrderLoader
            .InvoicesAsync(context, query.OrderId, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(OrderProjection.ToResponse(
            loaded.Value,
            invoices,
            OrderActor.Customer,
            includeInternal: false));
    }
}

/// <summary>Reads the shopper-visible timeline.</summary>
/// <param name="context">The Ordering data context.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class GetMyOrderTimelineQueryHandler(OrdersDbContext context, OrdersScope scope)
    : IQueryHandler<GetMyOrderTimelineQuery, IReadOnlyList<OrderEventResponse>>
{
    public async Task<Result<IReadOnlyList<OrderEventResponse>>> HandleAsync(
        GetMyOrderTimelineQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var loaded = await StoreOrders
            .LoadOwnAsync(context, scope, query.OrderId, tracked: false, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        IReadOnlyList<OrderEventResponse> timeline =
        [
            .. loaded.Value.Events
                .Where(entry => entry.IsCustomerVisible)
                .OrderBy(entry => entry.OccurredAt)
                .Select(OrderProjection.ToResponse),
        ];

        return Result.Success(timeline);
    }
}

/// <summary>
/// Cancels a whole order on the shopper's own say-so.
/// </summary>
/// <remarks>
/// It cancels every part that is still cancellable and leaves the rest, which is the only sensible
/// reading of "cancel my order" in a marketplace: one seller may have dispatched while the other has
/// not, and refusing the whole request because of the dispatched half would leave the shopper unable
/// to stop either.
/// </remarks>
/// <param name="context">The Ordering data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="workflow">The one place a sub-order moves.</param>
internal sealed class CancelMyOrderCommandHandler(
    OrdersDbContext context,
    OrdersScope scope,
    SubOrderWorkflow workflow) : ICommandHandler<CancelMyOrderCommand, OrderResponse>
{
    public async Task<Result<OrderResponse>> HandleAsync(
        CancelMyOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await StoreOrders
            .LoadOwnAsync(context, scope, command.OrderId, tracked: true, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var order = loaded.Value;

        var cancellable = order.SubOrders
            .Where(subOrder => SubOrderLifecycle.IsCustomerCancellable(subOrder.Status))
            .ToArray();

        if (cancellable.Length == 0)
        {
            return OrdersErrors.NotCancellable;
        }

        foreach (var subOrder in cancellable)
        {
            var cancelled = await workflow
                .CancelAsync(
                    order,
                    subOrder,
                    lines: null,
                    CancellationInitiator.Customer,
                    scope.ActorId,
                    command.Reason,
                    cancellationToken)
                .ConfigureAwait(false);

            if (cancelled.IsFailure)
            {
                return cancelled.Error;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var invoices = await OrderLoader
            .InvoicesAsync(context, order.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(OrderProjection.ToResponse(
            order,
            invoices,
            OrderActor.Customer,
            includeInternal: false));
    }
}

/// <summary>Cancels one seller's part, or some units of it, on the shopper's own say-so.</summary>
/// <param name="context">The Ordering data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="workflow">The one place a sub-order moves.</param>
internal sealed class CancelMySubOrderCommandHandler(
    OrdersDbContext context,
    OrdersScope scope,
    SubOrderWorkflow workflow) : ICommandHandler<CancelMySubOrderCommand, OrderResponse>
{
    public async Task<Result<OrderResponse>> HandleAsync(
        CancelMySubOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await OrderLoader
            .LoadSubOrderAsync(context, command.SubOrderId, tracked: true, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (order, subOrder) = loaded.Value;

        // The ownership check is here rather than in the query, and it is the same 404 a made-up id
        // gets: confirming that somebody else's sub-order exists is a disclosure.
        if (scope.CustomerId is not { } customerId || order.CustomerId != customerId)
        {
            return OrdersErrors.NotFound("order");
        }

        var cancelled = await workflow
            .CancelAsync(
                order,
                subOrder,
                command.Lines,
                CancellationInitiator.Customer,
                scope.ActorId,
                command.Reason,
                cancellationToken)
            .ConfigureAwait(false);

        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var invoices = await OrderLoader
            .InvoicesAsync(context, order.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(OrderProjection.ToResponse(
            order,
            invoices,
            OrderActor.Customer,
            includeInternal: false));
    }
}

/// <summary>Loading an order the storefront way: by (order, customer), never by id alone.</summary>
internal static class StoreOrders
{
    /// <summary>The caller's own order, or the same not-found a made-up id gets.</summary>
    /// <param name="context">The Ordering data context.</param>
    /// <param name="scope">Who is asking.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="tracked">Whether the caller intends to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Result<Order>> LoadOwnAsync(
        OrdersDbContext context,
        OrdersScope scope,
        Guid orderId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.CustomerId is not { } customerId)
        {
            return OrdersErrors.NotFound("order");
        }

        var loaded = await OrderLoader.LoadAsync(context, orderId, tracked, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        return loaded.Value.CustomerId == customerId
            ? loaded
            : OrdersErrors.NotFound("order");
    }
}
