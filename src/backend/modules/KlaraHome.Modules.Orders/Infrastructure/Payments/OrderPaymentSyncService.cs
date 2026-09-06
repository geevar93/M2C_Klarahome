using KlaraHome.Contracts.Orders;
using KlaraHome.Modules.Orders.Application;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure.Lifecycle;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Orders.Infrastructure.Payments;

/// <summary>
/// What the Payments module is allowed to do to an order (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The architecture diagram's <c>PAY-&gt;&gt;O: MarkPaid → Confirmed</c>, and the whole of it. Every
/// move goes through <see cref="SubOrderWorkflow"/>, exactly as the storefront's and the admin's do,
/// so a webhook confirming an order commits the same stock, raises the same events and writes the
/// same timeline as an operator confirming one by hand. A shortcut that set the status column
/// directly would be a second confirmation path, and the two would drift.
/// </para>
/// <para>
/// The transitions are taken as <see cref="OrderActor.System"/> — the one actor no HTTP caller can
/// claim to be. That is not a formality: <c>PendingPayment → Confirmed</c> is open to
/// <c>System</c> and <c>Platform</c> and to nobody else, so a customer cannot confirm their own
/// order by any route, however the request is shaped.
/// </para>
/// <para>
/// Every method is idempotent, because a webhook is delivered at least once and the reconciliation
/// sweep can reach the same fact independently. An order whose parts have already moved past
/// <c>PendingPayment</c> is a success rather than a conflict: the money is in, which is what the
/// caller was asserting.
/// </para>
/// <para>
/// It commits its own transaction. Payments calls it before writing its own row precisely so that a
/// failure between the two leaves the gateway event pending and the confirmation retryable — and
/// that only works if the confirmation is durable by the time this returns.
/// </para>
/// </remarks>
/// <param name="context">The Ordering data context.</param>
/// <param name="workflow">The single place a sub-order moves.</param>
/// <param name="logger">Reports what a payment fact moved.</param>
internal sealed partial class OrderPaymentSyncService(
    OrdersDbContext context,
    SubOrderWorkflow workflow,
    ILogger<OrderPaymentSyncService> logger) : IOrderPaymentSync
{
    /// <inheritdoc />
    public async Task<Result<OrderPaymentView>> GetAsync(
        Guid orderId,
        Guid? customerId,
        CancellationToken cancellationToken = default)
    {
        var order = await QueryFor(customerId)
            .Include(candidate => candidate.SubOrders)
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure<OrderPaymentView>(OrdersErrors.NotFound("order"));
        }

        return Result.Success(new OrderPaymentView(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.AmountPayable,
            order.CurrencyCode,
            order.PaymentMethod.ToString(),
            order.SubOrders.Any(subOrder =>
                subOrder.Status is SubOrderStatus.PendingPayment or SubOrderStatus.PaymentFailed),
            order.PaymentStatus is OrderPaymentStatus.Paid
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded,
            order.PlacedAt,
            order.CustomerSnapshot.DisplayName,
            order.CustomerSnapshot.Email,
            order.CustomerSnapshot.Mobile));
    }

    /// <inheritdoc />
    public async Task<Result> MarkPaidAsync(
        PaymentCaptureFact capture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);

        var order = await LoadAsync(capture.OrderId, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure(OrdersErrors.NotFound("order"));
        }

        // The mirror moves whether or not any part was still waiting, so a redelivery repairs a
        // status that a half-completed earlier attempt left behind.
        order.SetPaymentStatus(OrderPaymentStatus.Paid);

        var waiting = order.SubOrders
            .Where(subOrder => subOrder.Status
                is SubOrderStatus.PendingPayment
                or SubOrderStatus.PaymentFailed)
            .ToList();

        foreach (var subOrder in waiting)
        {
            // A failed attempt has to come back to awaiting-payment before it can be confirmed: the
            // machine has no edge from PaymentFailed straight to Confirmed, and inventing one here
            // would be inventing a second transition table.
            if (subOrder.Status == SubOrderStatus.PaymentFailed)
            {
                var reopened = await workflow
                    .TransitionAsync(
                        order,
                        subOrder,
                        SubOrderStatus.PendingPayment,
                        OrderActor.System,
                        actorId: null,
                        "The payment succeeded on a later attempt.",
                        cancellationToken)
                    .ConfigureAwait(false);

                if (reopened.IsFailure)
                {
                    return reopened;
                }
            }

            var confirmed = await workflow
                .TransitionAsync(
                    order,
                    subOrder,
                    SubOrderStatus.Confirmed,
                    OrderActor.System,
                    actorId: null,
                    $"Payment received: {capture.Method} {capture.Reference}.",
                    cancellationToken)
                .ConfigureAwait(false);

            if (confirmed.IsFailure)
            {
                return confirmed;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (waiting.Count > 0)
        {
            OrderConfirmed(logger, order.OrderNumber, waiting.Count, capture.AmountCaptured);
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> MarkPaymentFailedAsync(
        PaymentFailureFact failure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var order = await LoadAsync(failure.OrderId, cancellationToken).ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure(OrdersErrors.NotFound("order"));
        }

        // A failure arriving after the order was confirmed is a stale event about an earlier attempt.
        // Acting on it would un-pay a paid order, so it is ignored rather than refused: the caller is
        // relaying what the gateway said and has nothing to do differently.
        if (order.PaymentStatus is OrderPaymentStatus.Paid
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            return Result.Success();
        }

        order.SetPaymentStatus(OrderPaymentStatus.Failed);

        foreach (var subOrder in order.SubOrders
                     .Where(subOrder => subOrder.Status == SubOrderStatus.PendingPayment)
                     .ToList())
        {
            // Not cancelled. A declined card is a retry, and the unpaid-order sweeper is what
            // eventually cancels an order nobody comes back to — with the stock release that goes
            // with it.
            var moved = await workflow
                .TransitionAsync(
                    order,
                    subOrder,
                    SubOrderStatus.PaymentFailed,
                    OrderActor.System,
                    actorId: null,
                    failure.Reason ?? "The payment did not go through.",
                    cancellationToken)
                .ConfigureAwait(false);

            if (moved.IsFailure)
            {
                return moved;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RecordRefundAsync(
        PaymentRefundFact refund,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refund);

        var order = await context.Orders
            .FirstOrDefaultAsync(candidate => candidate.Id == refund.OrderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return Result.Failure(OrdersErrors.NotFound("order"));
        }

        // Derived from the two running totals rather than incremented, so a redelivered refund event
        // cannot walk the mirror past Refunded. Payments owns these figures; this column is only a
        // mirror, and when the two disagree Payments is right.
        order.SetPaymentStatus(refund.AmountRefunded <= 0m
            ? OrderPaymentStatus.Paid
            : refund.AmountRefunded >= refund.AmountCaptured
                ? OrderPaymentStatus.Refunded
                : OrderPaymentStatus.PartiallyRefunded);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Loads an order whole, with every sibling sub-order and its lines.
    /// </summary>
    /// <remarks>
    /// The query filters are bypassed on purpose. A webhook has no caller and no vendor scope, and a
    /// filtered read would return a vendor's half of a shared basket — from which the workflow would
    /// re-derive the parent order's status out of half its parts.
    /// </remarks>
    private async Task<Order?> LoadAsync(Guid orderId, CancellationToken cancellationToken)
        => await context.Orders
            .IgnoreQueryFilters()
            .Include(order => order.SubOrders)
            .ThenInclude(subOrder => subOrder.Lines)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// A shopper's own orders, or every order when the platform is asking.
    /// </summary>
    /// <remarks>
    /// The customer is matched in the predicate rather than checked afterwards, so an id belonging to
    /// somebody else does not resolve at all and answers the same 404 an invented one does.
    /// </remarks>
    private IQueryable<Order> QueryFor(Guid? customerId)
        => customerId is { } customer
            ? context.Orders.IgnoreQueryFilters().Where(order => order.CustomerId == customer)
            : context.Orders.IgnoreQueryFilters();

    [LoggerMessage(EventId = 1440, Level = LogLevel.Information,
        Message = "Order {OrderNumber} was confirmed on a payment of {Amount}: {Count} sub-order(s) moved.")]
    private static partial void OrderConfirmed(
        ILogger logger,
        string orderNumber,
        int count,
        decimal amount);
}
