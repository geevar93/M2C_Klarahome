using KlaraHome.Contracts.Orders;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Orders.Infrastructure.Events;

/// <summary>
/// Announces what happened to an order (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// The outbox is resolved <b>keyed by this module's context</b>, and that is not decoration. The
/// unkeyed registration is first-wins and belongs to whichever module registered first; enqueuing
/// through it here would add the row to a different context's change tracker, this module's
/// <c>SaveChangesAsync</c> would not write it, and the event would be lost with no error anywhere.
/// </para>
/// <para>
/// Nothing is saved here. The event becomes real when the caller's transaction commits, and not
/// before (ADR-003) — so Inventory never commits stock against an order that was, in the end, not
/// created, and no shopper is ever told about a confirmation that rolled back.
/// </para>
/// </remarks>
/// <param name="outbox">This module's outbox, keyed by its context.</param>
internal sealed class OrdersEventPublisher(
    [FromKeyedServices(typeof(OrdersDbContext))] IOutbox outbox)
{
    /// <summary>An order was created.</summary>
    /// <param name="order">The order, fully built.</param>
    public void Placed(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        outbox.Enqueue(new OrderPlaced(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.CartId,
            order.Status.ToString(),
            order.PaymentMethod.ToString(),
            order.GrandTotal,
            order.AmountPayable,
            order.CurrencyCode,
            [.. order.SubOrders.Select(subOrder => subOrder.VendorId ?? Guid.Empty)],
            order.PlacedAt));
    }

    /// <summary>One seller's part is theirs to fulfil.</summary>
    /// <param name="order">The order.</param>
    /// <param name="subOrder">The seller's part, already confirmed.</param>
    public void Confirmed(Order order, SubOrder subOrder)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(subOrder);

        outbox.Enqueue(new SubOrderConfirmed(
            order.Id,
            order.OrderNumber,
            subOrder.Id,
            subOrder.SubOrderNumber,
            subOrder.VendorId ?? Guid.Empty,
            order.CustomerId,
            subOrder.NetTotal,
            subOrder.CurrencyCode,
            subOrder.DispatchDueAt,
            [.. subOrder.Lines.Where(line => line.QuantityLive > 0).Select(Fact)]));
    }

    /// <summary>A sub-order moved.</summary>
    /// <param name="order">The order, already re-derived.</param>
    /// <param name="subOrder">The seller's part.</param>
    /// <param name="from">Where it was.</param>
    /// <param name="actor">Who moved it.</param>
    /// <param name="reason">Why, when a reason was given.</param>
    public void StatusChanged(
        Order order,
        SubOrder subOrder,
        SubOrderStatus from,
        OrderActor actor,
        string? reason)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(subOrder);

        outbox.Enqueue(new SubOrderStatusChanged(
            order.Id,
            order.OrderNumber,
            subOrder.Id,
            subOrder.SubOrderNumber,
            subOrder.VendorId ?? Guid.Empty,
            order.CustomerId,
            from.ToString(),
            subOrder.Status.ToString(),
            order.Status.ToString(),
            ActorName(actor),
            reason));
    }

    /// <summary>Units were cancelled.</summary>
    /// <param name="order">The order.</param>
    /// <param name="subOrder">The seller's part.</param>
    /// <param name="initiator">Who asked.</param>
    /// <param name="reason">Why.</param>
    /// <param name="isPartial">Whether some of the sub-order survives.</param>
    /// <param name="wasConfirmed">Whether the stock behind it had already been committed.</param>
    /// <param name="cancelledTotal">What is coming off the bill.</param>
    /// <param name="lines">The units this cancellation covers.</param>
    public void Cancelled(
        Order order,
        SubOrder subOrder,
        CancellationInitiator initiator,
        string? reason,
        bool isPartial,
        bool wasConfirmed,
        decimal cancelledTotal,
        IReadOnlyList<OrderLineFact> lines)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(subOrder);

        outbox.Enqueue(new SubOrderCancelled(
            order.Id,
            order.OrderNumber,
            subOrder.Id,
            subOrder.SubOrderNumber,
            subOrder.VendorId ?? Guid.Empty,
            order.CustomerId,
            initiator.ToString().ToLowerInvariant(),
            reason,
            isPartial,
            wasConfirmed,
            cancelledTotal,
            subOrder.CurrencyCode,
            lines));
    }

    /// <summary>A tax invoice was raised.</summary>
    /// <param name="order">The order.</param>
    /// <param name="invoice">The invoice.</param>
    public void Invoiced(Order order, Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(invoice);

        outbox.Enqueue(new InvoiceIssued(
            invoice.OrderId,
            invoice.SubOrderId,
            invoice.VendorId ?? Guid.Empty,
            order.CustomerId,
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.FinancialYear,
            invoice.TaxableValue,
            invoice.Total,
            invoice.CurrencyCode,
            invoice.FileId,
            invoice.IssuedAt));
    }

    /// <summary>An order reached the end of its life.</summary>
    /// <param name="order">The order, already completed.</param>
    public void Completed(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        outbox.Enqueue(new OrderCompleted(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.NetTotal,
            order.CurrencyCode,
            order.CompletedAt ?? DateTimeOffset.UtcNow));
    }

    /// <summary>Projects a line into the flat fact the events carry.</summary>
    private static OrderLineFact Fact(OrderLine line)
        => new(line.Id, line.ListingId, line.Sku, line.QuantityLive, line.LineTotal - line.CancelledValue);

    /// <summary>The wire name of an actor, lowercase, as the contracts document it.</summary>
    private static string ActorName(OrderActor actor)
        => actor switch
        {
            OrderActor.Customer => "customer",
            OrderActor.Vendor => "vendor",
            OrderActor.Platform => "platform",
            _ => "system",
        };
}
