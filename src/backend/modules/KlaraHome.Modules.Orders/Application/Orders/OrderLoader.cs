using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Orders.Application.Orders;

/// <summary>
/// Loads an order the way every handler in this module needs it.
/// </summary>
/// <remarks>
/// <para>
/// Always the whole aggregate — order, sub-orders and lines — even when the caller asked about one
/// sub-order. The state machine re-derives the order's status from <em>every</em> sibling, and a
/// partially loaded order would derive it from half of them and quietly write the answer back.
/// </para>
/// <para>
/// A vendor caller still gets the whole order object, but the vendor query filter means its
/// <c>SubOrders</c> collection holds only theirs. That is safe for a transition, which touches one
/// sub-order, and it is <em>not</em> safe for the derivation — so the loader marks it, and the
/// handlers that would write a derived status refuse to run on a partially visible order.
/// </para>
/// </remarks>
internal static class OrderLoader
{
    /// <summary>The full aggregate, or a not-found.</summary>
    /// <param name="context">The Ordering data context.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="tracked">Whether the caller intends to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Result<Order>> LoadAsync(
        OrdersDbContext context,
        Guid orderId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var order = await Query(context, tracked)
            .FirstOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken)
            .ConfigureAwait(false);

        return order is null ? OrdersErrors.NotFound("order") : Result.Success(order);
    }

    /// <summary>
    /// The order one sub-order belongs to, together with that sub-order.
    /// </summary>
    /// <remarks>
    /// The sub-order is found <em>through</em> the loaded order rather than fetched separately, so
    /// the instance the caller mutates is the one the change tracker is holding. Fetching it twice
    /// is the classic way to write a transition that silently does nothing.
    /// </remarks>
    /// <param name="context">The Ordering data context.</param>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="tracked">Whether the caller intends to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Result<(Order Order, SubOrder SubOrder)>> LoadSubOrderAsync(
        OrdersDbContext context,
        Guid subOrderId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The vendor filter applies here too, so a seller asking about somebody else's sub-order gets
        // the same answer as one asking about an id that never existed.
        var orderId = await context.SubOrders
            .AsNoTracking()
            .Where(subOrder => subOrder.Id == subOrderId)
            .Select(subOrder => (Guid?)subOrder.OrderId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (orderId is null)
        {
            return OrdersErrors.NotFound("order");
        }

        var loaded = await LoadAsync(context, orderId.Value, tracked, cancellationToken).ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var found = loaded.Value.SubOrders.FirstOrDefault(subOrder => subOrder.Id == subOrderId);

        return found is null
            ? OrdersErrors.NotFound("order")
            : Result.Success((loaded.Value, found));
    }

    /// <summary>Every invoice on an order, keyed by the sub-order it covers.</summary>
    /// <param name="context">The Ordering data context.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<IReadOnlyDictionary<Guid, Invoice>> InvoicesAsync(
        OrdersDbContext context,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rows = await context.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.OrderId == orderId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(invoice => invoice.SubOrderId);
    }

    /// <summary>The aggregate query, tracked or not.</summary>
    private static IQueryable<Order> Query(OrdersDbContext context, bool tracked)
    {
        var orders = context.Orders
            .Include(order => order.SubOrders)
            .ThenInclude(subOrder => subOrder.Lines)
            .Include(order => order.Events)
            .AsSplitQuery();

        return tracked ? orders : orders.AsNoTracking();
    }
}
