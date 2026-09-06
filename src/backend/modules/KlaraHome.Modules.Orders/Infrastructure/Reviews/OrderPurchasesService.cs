using KlaraHome.Contracts.Orders;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Orders.Infrastructure.Reviews;

/// <summary>
/// Answers <see cref="IOrderPurchases"/> from this module's own tables.
/// </summary>
/// <remarks>
/// <para>
/// The fifth and smallest of ordering's outward seams. It reads three tables, returns a handful of
/// columns, and cannot write anything — which is the shape a proof-of-purchase check should have.
/// The Reviews module asks it one question, "did this person receive this", and the answer is a row
/// or nothing.
/// </para>
/// <para>
/// Delivery is read off the sub-order rather than the line, because that is where this platform
/// records it: a courier delivers a parcel, and a parcel is a seller's part of an order. A line
/// inside a delivered sub-order was delivered, and there is no state in which one line of a parcel
/// arrived and another did not — a short delivery is a return, and it is recorded as one.
/// </para>
/// <para>
/// Fully cancelled and fully returned lines are excluded. Both are things the customer does not
/// have, and a review written against something that went back is a review of an experience the
/// returns process has already recorded. A <em>partly</em> returned line is kept: the shopper still
/// has some of it, and their opinion of it is real.
/// </para>
/// </remarks>
/// <param name="context">The Ordering data context.</param>
internal sealed class OrderPurchasesService(OrdersDbContext context) : IOrderPurchases
{
    /// <inheritdoc />
    public async ValueTask<PurchasedLine?> FindDeliveredLineAsync(
        Guid orderLineId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        // The customer is part of the predicate rather than checked afterwards. A line read first and
        // filtered second is one refactor away from being returned to the wrong person.
        var rows = await Query(customerId)
            .Where(row => row.Line.Id == orderLineId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Count == 0 ? null : Project(rows[0]);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PurchasedLine>> ListDeliveredLinesAsync(
        Guid customerId,
        Guid? variantId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = Query(customerId);

        if (variantId is { } variant)
        {
            query = query.Where(row => row.Line.VariantId == variant);
        }

        var rows = await query
            // Newest delivery first: the purchase somebody is most likely to want to write about is
            // the one that arrived this week.
            .OrderByDescending(row => row.SubOrder.DeliveredAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(Project)];
    }

    /// <summary>
    /// Every line this customer received, joined to the sub-order that carries the delivery and the
    /// order that carries its number.
    /// </summary>
    /// <remarks>
    /// A join rather than three round trips because the caller needs the seller and the order number
    /// on every row, and both are one hop away. The global tenant filter applies to all three sets.
    /// </remarks>
    /// <param name="customerId">The shopper whose purchases these are.</param>
    private IQueryable<PurchaseRow> Query(Guid customerId)
        => from line in context.OrderLines.AsNoTracking()
           join subOrder in context.SubOrders.AsNoTracking() on line.SubOrderId equals subOrder.Id
           join order in context.Orders.AsNoTracking() on subOrder.OrderId equals order.Id
           where order.CustomerId == customerId
                 && subOrder.DeliveredAt != null
                 && subOrder.VendorId != null
                 && line.Status != OrderLineStatus.Cancelled
                 && line.Status != OrderLineStatus.Returned
           select new PurchaseRow(line, subOrder, order);

    /// <summary>Turns a joined row into the contract's shape.</summary>
    /// <param name="row">The joined line, sub-order and order.</param>
    private static PurchasedLine Project(PurchaseRow row)
        => new(
            row.Line.Id,
            row.Order.Id,
            row.Order.OrderNumber,
            row.SubOrder.Id,
            row.SubOrder.VendorId!.Value,
            row.Line.ListingId,
            row.Line.VariantId,
            row.Line.Sku,
            row.Line.Snapshot.Name,
            // What the customer is left holding, which is what they can honestly review.
            Math.Max(0, row.Line.QuantityLive - row.Line.QuantityReturned),
            row.SubOrder.DeliveredAt!.Value);

    /// <summary>The join's shape. Internal to this file; nothing outside it sees an entity.</summary>
    /// <param name="Line">The order line.</param>
    /// <param name="SubOrder">The seller's part of the order, which carries the delivery.</param>
    /// <param name="Order">The order, which carries the customer and the number.</param>
    private sealed record PurchaseRow(OrderLine Line, SubOrder SubOrder, Order Order);
}
