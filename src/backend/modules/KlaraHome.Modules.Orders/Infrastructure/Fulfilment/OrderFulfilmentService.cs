using KlaraHome.Contracts.Orders;
using KlaraHome.Modules.Orders.Application;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure.Lifecycle;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Orders.Infrastructure.Fulfilment;

/// <summary>
/// What the Shipping module is allowed to do to an order (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <c>OrderPaymentSyncService</c> and for the same reasons. Every move goes
/// through <see cref="SubOrderWorkflow"/>, so a courier scan that delivers a parcel starts the
/// return window, writes the timeline, re-derives the parent order's status and raises the events
/// exactly as an operator moving it by hand would. A shortcut that set the column directly would be
/// a second fulfilment path, and the two would drift.
/// </para>
/// <para>
/// The transitions are taken as <see cref="OrderActor.System"/>, which is the one actor no HTTP
/// caller can claim. That matters here more than anywhere: <c>OutForDelivery → Delivered</c> is open
/// to <c>System</c> and <c>Platform</c> and to nobody else, so no customer and no seller can declare
/// their own parcel delivered however the request is shaped.
/// </para>
/// <para>
/// A transition the machine does not have is refused rather than invented. A courier reporting
/// movement on a sub-order that was cancelled yesterday is a discrepancy for a human to look at, and
/// the caller records it as a timeline note instead — which is why <see cref="NoteAsync"/> exists
/// alongside <see cref="AdvanceAsync"/> rather than being folded into it.
/// </para>
/// </remarks>
/// <param name="context">The Ordering data context.</param>
/// <param name="workflow">The single place a sub-order moves.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what a courier fact moved.</param>
internal sealed partial class OrderFulfilmentService(
    OrdersDbContext context,
    SubOrderWorkflow workflow,
    IClock clock,
    ILogger<OrderFulfilmentService> logger) : IOrderFulfilment
{
    /// <inheritdoc />
    public async Task<Result<SubOrderFulfilmentView>> GetAsync(
        Guid subOrderId,
        CancellationToken cancellationToken = default)
    {
        var order = await ReadQuery()
            .FirstOrDefaultAsync(
                candidate => candidate.SubOrders.Any(subOrder => subOrder.Id == subOrderId),
                cancellationToken)
            .ConfigureAwait(false);

        var found = order?.SubOrders.FirstOrDefault(subOrder => subOrder.Id == subOrderId);

        return order is null || found is null
            ? Result.Failure<SubOrderFulfilmentView>(OrdersErrors.NotFound("sub-order"))
            : Result.Success(Project(order, found));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubOrderFulfilmentView>> GetManyAsync(
        IReadOnlyCollection<Guid> subOrderIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subOrderIds);

        if (subOrderIds.Count == 0)
        {
            return [];
        }

        var wanted = subOrderIds.Distinct().ToArray();

        // One query for the whole pick list. The alternative — a read per sub-order — turns a
        // morning of orders into a morning of round trips.
        var orders = await ReadQuery()
            .Where(order => order.SubOrders.Any(subOrder => wanted.Contains(subOrder.Id)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. orders.SelectMany(
                order => order.SubOrders.Where(subOrder => wanted.Contains(subOrder.Id)),
                Project),
        ];
    }

    /// <inheritdoc />
    public async Task<Result> AdvanceAsync(
        Guid subOrderId,
        string status,
        string? note,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<SubOrderStatus>(status, ignoreCase: true, out var next))
        {
            return Result.Failure(OrdersErrors.UnknownStatus);
        }

        var (order, subOrder) = await LoadAsync(subOrderId, cancellationToken).ConfigureAwait(false);

        if (order is null || subOrder is null)
        {
            return Result.Failure(OrdersErrors.NotFound("sub-order"));
        }

        // Idempotent by design. A tracking webhook is delivered at least once and the polling
        // fallback can reach the same scan independently, so a parcel already delivered is a
        // success — the caller was asserting a fact that is already true.
        if (subOrder.Status == next)
        {
            return Result.Success();
        }

        var from = subOrder.Status;

        var moved = await workflow
            .TransitionAsync(order, subOrder, next, OrderActor.System, actorId: null, note, cancellationToken)
            .ConfigureAwait(false);

        if (moved.IsFailure)
        {
            return moved;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        CourierMoved(logger, subOrder.SubOrderNumber, from, next);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> NoteAsync(
        Guid subOrderId,
        string note,
        CancellationToken cancellationToken = default)
    {
        var (order, subOrder) = await LoadAsync(subOrderId, cancellationToken).ConfigureAwait(false);

        if (order is null || subOrder is null)
        {
            return Result.Failure(OrdersErrors.NotFound("sub-order"));
        }

        // Customer-visible. A courier scan is the shopper's own tracking, and an entry they cannot
        // see would leave "track my order" silent between dispatch and delivery.
        order.Record(OrderEvent.Record(
            order.Id,
            subOrder.Id,
            OrderEventTypes.Note,
            OrderActor.System,
            actorId: null,
            note,
            payload: null,
            visible: true,
            clock.UtcNow));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// The reading query: whole orders, unfiltered, with the lines a consignment is built from.
    /// </summary>
    /// <remarks>
    /// The query filters are bypassed on purpose. A courier webhook has no caller and no vendor
    /// scope, and a filtered read would return whichever seller the ambient context happened to name
    /// — which for a two-seller order is half a parcel.
    /// </remarks>
    private IQueryable<Order> ReadQuery()
        => context.Orders
            .IgnoreQueryFilters()
            .Include(order => order.SubOrders)
            .ThenInclude(subOrder => subOrder.Lines)
            .AsNoTracking();

    private async Task<(Order? Order, SubOrder? SubOrder)> LoadAsync(
        Guid subOrderId,
        CancellationToken cancellationToken)
    {
        var order = await context.Orders
            .IgnoreQueryFilters()
            .Include(candidate => candidate.SubOrders)
            .ThenInclude(subOrder => subOrder.Lines)
            .FirstOrDefaultAsync(
                candidate => candidate.SubOrders.Any(subOrder => subOrder.Id == subOrderId),
                cancellationToken)
            .ConfigureAwait(false);

        return (order, order?.SubOrders.FirstOrDefault(subOrder => subOrder.Id == subOrderId));
    }

    /// <summary>
    /// What a consignment is booked from, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cash figure is the sub-order's <em>net</em> total — what is left after any partial
    /// cancellation — because that is what the courier must actually collect. Handing over the gross
    /// figure would have the courier take money for units the shopper was already told were gone.
    /// </para>
    /// <para>
    /// Cancelled units are dropped from the lines for the same reason: a pick list that lists them is
    /// a packer looking for something that is not going anywhere.
    /// </para>
    /// </remarks>
    private static SubOrderFulfilmentView Project(Order order, SubOrder subOrder)
    {
        var isCod = order.PaymentMethod == OrderPaymentMethod.CashOnDelivery;
        var net = subOrder.NetTotal;

        return new SubOrderFulfilmentView(
            order.Id,
            order.OrderNumber,
            subOrder.Id,
            subOrder.SubOrderNumber,
            subOrder.VendorId ?? Guid.Empty,
            order.CustomerId,
            subOrder.Status.ToString(),
            order.PaymentMethod.ToString(),
            isCod,
            isCod ? net : 0m,
            net,
            subOrder.CurrencyCode,
            subOrder.DispatchDueAt,
            subOrder.ShippingOptionCode,
            new FulfilmentAddress(
                order.ShippingAddress.RecipientName,
                order.ShippingAddress.Mobile,
                order.ShippingAddress.Line1,
                order.ShippingAddress.Line2,
                order.ShippingAddress.Landmark,
                order.ShippingAddress.City,
                order.ShippingAddress.StateId,
                order.ShippingAddress.Pincode),
            [
                .. subOrder.Lines
                    .Where(line => line.QuantityLive > 0)
                    .Select(line => new FulfilmentLine(
                        line.Id,
                        line.Sku,
                        line.Snapshot.Name,
                        line.QuantityLive,
                        line.Snapshot.WeightGrams,
                        line.LineTotal - line.CancelledValue,
                        line.WarehouseId)),
            ]);
    }

    [LoggerMessage(EventId = 1460, Level = LogLevel.Information,
        Message = "Courier movement moved sub-order {SubOrderNumber} from {FromStatus} to {ToStatus}.")]
    private static partial void CourierMoved(
        ILogger logger,
        string subOrderNumber,
        SubOrderStatus fromStatus,
        SubOrderStatus toStatus);
}
