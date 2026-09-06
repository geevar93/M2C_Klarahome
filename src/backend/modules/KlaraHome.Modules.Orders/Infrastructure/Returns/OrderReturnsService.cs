using KlaraHome.Contracts.Orders;
using KlaraHome.Modules.Orders.Application;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure.Lifecycle;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Orders.Infrastructure.Returns;

/// <summary>
/// Answers <see cref="IOrderReturns"/> from this module's own tables.
/// </summary>
/// <remarks>
/// <para>
/// The third of ordering's outward seams, and the narrowest of the three. It hands out what a line
/// is still worth and how much of it is left, takes the three return states the machine has, and
/// records what actually came back — and nothing else. In particular it does not let a caller change
/// a price, cancel anything, or move a sub-order anywhere the return leg does not go.
/// </para>
/// <para>
/// Every read passes a customer id through rather than trusting the caller to filter afterwards. A
/// shopper's return screen and an operator's queue read the same method, and the only difference
/// between them is whether that argument is null — which makes the confinement one line rather than
/// a habit somebody can forget.
/// </para>
/// </remarks>
/// <param name="context">The Ordering data context.</param>
/// <param name="workflow">The one place a sub-order moves.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what came back.</param>
internal sealed partial class OrderReturnsService(
    OrdersDbContext context,
    SubOrderWorkflow workflow,
    IClock clock,
    ILogger<OrderReturnsService> logger) : IOrderReturns
{
    /// <summary>The only states this seam may move a sub-order into.</summary>
    /// <remarks>
    /// A whitelist rather than a call straight through to the machine. The transition table would
    /// refuse most of the rest anyway; what this adds is that a bug in the Returns module cannot
    /// cancel an order, whatever it asks for.
    /// </remarks>
    private static readonly SubOrderStatus[] ReturnStates =
    [
        SubOrderStatus.ReturnRequested,
        SubOrderStatus.ReturnInProgress,
        SubOrderStatus.Returned,
    ];

    /// <inheritdoc />
    public async Task<Result<SubOrderReturnView>> GetAsync(
        Guid subOrderId,
        Guid? customerId,
        CancellationToken cancellationToken = default)
    {
        var order = await ReadQuery(customerId)
            .FirstOrDefaultAsync(
                candidate => candidate.SubOrders.Any(subOrder => subOrder.Id == subOrderId),
                cancellationToken)
            .ConfigureAwait(false);

        var found = order?.SubOrders.FirstOrDefault(subOrder => subOrder.Id == subOrderId);

        if (order is null || found is null)
        {
            return Result.Failure<SubOrderReturnView>(OrdersErrors.NotFound("sub-order"));
        }

        var invoice = await InvoiceAsync(found.Id, cancellationToken).ConfigureAwait(false);

        return Result.Success(Project(order, found, invoice));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubOrderReturnView>> GetManyAsync(
        IReadOnlyCollection<Guid> subOrderIds,
        Guid? customerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subOrderIds);

        if (subOrderIds.Count == 0)
        {
            return [];
        }

        var wanted = subOrderIds.Distinct().ToArray();

        var orders = await ReadQuery(customerId)
            .Where(order => order.SubOrders.Any(subOrder => wanted.Contains(subOrder.Id)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var invoices = await context.Invoices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(invoice => wanted.Contains(invoice.SubOrderId))
            .Select(invoice => new { invoice.SubOrderId, invoice.Id, invoice.InvoiceNumber })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var bySubOrder = invoices.ToDictionary(
            invoice => invoice.SubOrderId,
            invoice => ((Guid?)invoice.Id, (string?)invoice.InvoiceNumber));

        return
        [
            .. orders.SelectMany(
                order => order.SubOrders.Where(subOrder => wanted.Contains(subOrder.Id)),
                (order, subOrder) => Project(
                    order,
                    subOrder,
                    bySubOrder.GetValueOrDefault(subOrder.Id))),
        ];
    }

    /// <inheritdoc />
    public async Task<Result> AdvanceAsync(
        Guid subOrderId,
        string status,
        string? note,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<SubOrderStatus>(status, ignoreCase: true, out var next)
            || !ReturnStates.Contains(next))
        {
            return Result.Failure(OrdersErrors.UnknownStatus);
        }

        var (order, subOrder) = await LoadAsync(subOrderId, cancellationToken).ConfigureAwait(false);

        if (order is null || subOrder is null)
        {
            return Result.Failure(OrdersErrors.NotFound("sub-order"));
        }

        if (subOrder.Status == next)
        {
            return Result.Success();
        }

        var moved = await workflow
            .TransitionAsync(order, subOrder, next, OrderActor.System, actorId: null, note, cancellationToken)
            .ConfigureAwait(false);

        if (moved.IsFailure)
        {
            return moved;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<int>> RecordReturnedAsync(
        Guid subOrderId,
        IReadOnlyCollection<ReturnedUnits> units,
        string? note,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(units);

        if (units.Count == 0)
        {
            return Result.Success(0);
        }

        var (order, subOrder) = await LoadAsync(subOrderId, cancellationToken).ConfigureAwait(false);

        if (order is null || subOrder is null)
        {
            return Result.Failure<int>(OrdersErrors.NotFound("sub-order"));
        }

        var recorded = 0;

        foreach (var wanted in units)
        {
            var line = subOrder.Lines.FirstOrDefault(candidate => candidate.Id == wanted.OrderLineId);

            // Clamped by the line itself rather than refused here. A caller asking for five of the
            // three that are left means "all of them", and a return that had already been recorded
            // takes nothing — which is exactly what makes a redelivered event harmless.
            recorded += line?.RecordReturn(wanted.Quantity) ?? 0;
        }

        if (recorded == 0)
        {
            return Result.Success(0);
        }

        order.Record(OrderEvent.Record(
            order.Id,
            subOrder.Id,
            OrderEventTypes.Note,
            OrderActor.System,
            actorId: null,
            note ?? $"{recorded} unit(s) were returned.",
            payload: null,
            visible: true,
            clock.UtcNow));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        UnitsReturned(logger, subOrder.SubOrderNumber, recorded);

        return Result.Success(recorded);
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

    /// <summary>The read query, confined to one shopper's orders when a shopper is asking.</summary>
    private IQueryable<Order> ReadQuery(Guid? customerId)
    {
        var orders = context.Orders
            .IgnoreQueryFilters()
            .Include(order => order.SubOrders)
            .ThenInclude(subOrder => subOrder.Lines)
            .AsNoTracking();

        return customerId is { } customer
            ? orders.Where(order => order.CustomerId == customer)
            : orders;
    }

    /// <summary>Loads an order and one of its parts, tracked and ready to be moved.</summary>
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

    /// <summary>The invoice raised against one seller's part, which a credit note credits.</summary>
    private async Task<(Guid? Id, string? Number)> InvoiceAsync(
        Guid subOrderId,
        CancellationToken cancellationToken)
    {
        var invoice = await context.Invoices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(candidate => candidate.SubOrderId == subOrderId)
            .Select(candidate => new { candidate.Id, candidate.InvoiceNumber })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return invoice is null ? (null, null) : (invoice.Id, invoice.InvoiceNumber);
    }

    /// <summary>Projects a seller's part into what the Returns module needs.</summary>
    private static SubOrderReturnView Project(
        Order order,
        SubOrder subOrder,
        (Guid? Id, string? Number) invoice)
    {
        var address = order.ShippingAddress;

        // Intra-state is derived from the tax actually charged rather than by re-comparing the
        // seller's state to the place of supply, exactly as the invoice derives it: IGST on a line
        // means the supply crossed a state border, and that is the fact the credit note must repeat.
        var isIntraState = subOrder.Lines.Sum(line => line.Igst) == 0m;

        var isPaid = order.PaymentStatus
            is OrderPaymentStatus.Paid
            or OrderPaymentStatus.PartiallyRefunded;

        return new SubOrderReturnView(
            order.Id,
            order.OrderNumber,
            subOrder.Id,
            subOrder.SubOrderNumber,
            subOrder.VendorId ?? Guid.Empty,
            subOrder.VendorName,
            subOrder.VendorGstin,
            order.CustomerId,
            subOrder.Status.ToString(),
            order.PaymentMethod.ToString(),
            isPaid,
            subOrder.ShippingTotal,
            subOrder.ShippingTax,
            subOrder.Total,
            subOrder.CurrencyCode,
            order.PlaceOfSupplyStateId,
            isIntraState,
            subOrder.DeliveredAt,
            subOrder.ReturnWindowEndsAt,
            invoice.Id,
            invoice.Number,
            new FulfilmentAddress(
                address.RecipientName,
                address.Mobile,
                address.Line1,
                address.Line2,
                address.Landmark,
                address.City,
                address.StateId,
                address.Pincode),
            [.. subOrder.Lines.Select(ToReturnable)]);
    }

    /// <summary>Projects one line, with the tax that was charged on it.</summary>
    private static ReturnableLine ToReturnable(OrderLine line)
        => new(
            line.Id,
            line.ListingId,
            line.VariantId,
            line.Sku,
            line.Snapshot.Name,
            line.Snapshot.ImageFileId,
            line.Snapshot.HsnCode,
            line.Quantity,
            line.QuantityCancelled,
            line.QuantityReturned,
            line.UnitPrice,
            line.TaxableValue,
            line.GstRate,
            line.Cgst,
            line.Sgst,
            line.Igst,
            line.Cess,
            line.LineTotal,
            line.Snapshot.IsReturnable,
            line.Snapshot.ReturnWindowDays);

    [LoggerMessage(EventId = 1480, Level = LogLevel.Information,
        Message = "{Units} unit(s) were recorded as returned against sub-order {SubOrderNumber}.")]
    private static partial void UnitsReturned(ILogger logger, string subOrderNumber, int units);
}
