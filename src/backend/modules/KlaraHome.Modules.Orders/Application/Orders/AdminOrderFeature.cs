using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure;
using KlaraHome.Modules.Orders.Infrastructure.Lifecycle;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Orders.Application.Orders;

/// <summary>Lists orders for platform staff, or a seller's own, newest first.</summary>
/// <param name="Status">Restrict to one derived order status.</param>
/// <param name="PaymentStatus">Restrict to one payment status.</param>
/// <param name="CustomerId">Restrict to one shopper.</param>
/// <param name="Number">Find one by its order number, in whole or in part.</param>
/// <param name="From">Only orders placed on or after this instant.</param>
/// <param name="To">Only orders placed before this instant.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListOrdersQuery(
    string? Status,
    string? PaymentStatus,
    Guid? CustomerId,
    string? Number,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Size) : IQuery<PagedResult<OrderSummaryResponse>>;

/// <summary>Reads one order in full, timeline and operator notes included.</summary>
/// <param name="OrderId">The order.</param>
internal sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderResponse>;

/// <summary>
/// The fulfilment worklist: sub-orders by state, a seller's own when the caller is one.
/// </summary>
/// <param name="Status">Restrict to one sub-order status.</param>
/// <param name="VendorId">Restrict to one seller. Ignored for a vendor caller, who has only their own.</param>
/// <param name="WarehouseId">Restrict to the lines allocated to one warehouse, which is how a
/// single site works its own queue rather than the whole network's.</param>
/// <param name="OverdueOnly">Only sub-orders whose dispatch promise has already passed.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListSubOrdersQuery(
    string? Status,
    Guid? VendorId,
    Guid? WarehouseId,
    bool? OverdueOnly,
    string? Cursor,
    int? Size) : IQuery<PagedResult<SubOrderResponse>>;

/// <summary>Moves one seller's part along the lifecycle.</summary>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="Status">Where to move it.</param>
/// <param name="Reason">Why, when a reason is called for.</param>
internal sealed record TransitionSubOrderCommand(Guid SubOrderId, string Status, string? Reason)
    : ICommand<SubOrderResponse>;

/// <summary>Cancels one seller's part, or some units of it, as a seller or as Operations.</summary>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="Reason">Why. Required once the parcel has been dispatched.</param>
/// <param name="Lines">Which units, or null for all of them.</param>
internal sealed record CancelSubOrderCommand(
    Guid SubOrderId,
    string? Reason,
    IReadOnlyList<CancellationLine>? Lines) : ICommand<SubOrderResponse>;

/// <summary>Raises the tax invoice for one seller's part, by hand.</summary>
/// <param name="SubOrderId">The seller's part.</param>
internal sealed record IssueInvoiceCommand(Guid SubOrderId) : ICommand<InvoiceResponse>;

/// <summary>Appends an internal note to an order's timeline.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="Message">What to write down.</param>
/// <param name="IsCustomerVisible">Whether the shopper sees it. Off by default.</param>
internal sealed record AddOrderNoteCommand(Guid OrderId, string Message, bool IsCustomerVisible)
    : ICommand<OrderResponse>;

/// <summary>Rejects a transition that could never be attempted.</summary>
internal sealed class TransitionSubOrderValidator : AbstractValidator<TransitionSubOrderCommand>
{
    public TransitionSubOrderValidator()
    {
        RuleFor(command => command.SubOrderId).NotEmpty();
        RuleFor(command => command.Status).NotEmpty().MaximumLength(24);
        RuleFor(command => command.Reason).MaximumLength(500);
    }
}

/// <summary>Rejects a cancellation that could never be attempted.</summary>
internal sealed class CancelSubOrderValidator : AbstractValidator<CancelSubOrderCommand>
{
    public CancelSubOrderValidator()
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

/// <summary>Rejects a note that could never be written.</summary>
internal sealed class AddOrderNoteValidator : AbstractValidator<AddOrderNoteCommand>
{
    public AddOrderNoteValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.Message).NotEmpty().MaximumLength(1000);
    }
}

/// <summary>
/// Lists orders.
/// </summary>
/// <remarks>
/// One handler for two audiences, separated by one predicate. A vendor caller sees only orders that
/// have a sub-order of theirs, and the filter is expressed as "has any visible sub-order" — the
/// vendor query filter decides what visible means, so this handler cannot get the scoping wrong by
/// forgetting a clause.
/// </remarks>
/// <param name="context">The Ordering data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListOrdersQueryHandler(
    OrdersDbContext context,
    OrdersScope scope,
    IOptions<OrdersOptions> options)
    : IQueryHandler<ListOrdersQuery, PagedResult<OrderSummaryResponse>>
{
    public async Task<Result<PagedResult<OrderSummaryResponse>>> HandleAsync(
        ListOrdersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);

        var rows = context.Orders
            .AsNoTracking()
            .Include(order => order.SubOrders)
            .ThenInclude(subOrder => subOrder.Lines)
            .AsSplitQuery()
            .AsQueryable();

        if (scope.IsVendor)
        {
            rows = rows.Where(order => order.SubOrders.Count > 0);
        }

        if (Enum.TryParse<OrderStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(order => order.Status == status);
        }

        if (Enum.TryParse<OrderPaymentStatus>(query.PaymentStatus, ignoreCase: true, out var paymentStatus))
        {
            rows = rows.Where(order => order.PaymentStatus == paymentStatus);
        }

        if (query.CustomerId is { } customerId)
        {
            rows = rows.Where(order => order.CustomerId == customerId);
        }

        if (!string.IsNullOrWhiteSpace(query.Number))
        {
            var number = query.Number.Trim().ToUpperInvariant();
            rows = rows.Where(order => order.OrderNumber.Contains(number));
        }

        if (query.From is { } from)
        {
            rows = rows.Where(order => order.PlacedAt >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(order => order.PlacedAt < to);
        }

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
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<OrderSummaryResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads one order in full.</summary>
/// <param name="context">The Ordering data context.</param>
/// <param name="scope">Who is asking; decides the offered next states.</param>
internal sealed class GetOrderQueryHandler(OrdersDbContext context, OrdersScope scope)
    : IQueryHandler<GetOrderQuery, OrderResponse>
{
    public async Task<Result<OrderResponse>> HandleAsync(
        GetOrderQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var loaded = await OrderLoader
            .LoadAsync(context, query.OrderId, tracked: false, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        // A vendor caller whose filter left no sub-orders has no part in this order, and answering
        // with the shell — the customer's name, their address, the total — would be a disclosure.
        if (scope.IsVendor && loaded.Value.SubOrders.Count == 0)
        {
            return OrdersErrors.NotFound("order");
        }

        var invoices = await OrderLoader
            .InvoicesAsync(context, query.OrderId, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(OrderProjection.ToResponse(
            loaded.Value,
            invoices,
            scope.Actor,
            includeInternal: true));
    }
}

/// <summary>Lists sub-orders: the fulfilment worklist.</summary>
/// <param name="context">The Ordering data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="options">Supplies the page ceiling.</param>
/// <param name="clock">The sanctioned clock, for the overdue filter.</param>
internal sealed class ListSubOrdersQueryHandler(
    OrdersDbContext context,
    OrdersScope scope,
    IOptions<OrdersOptions> options,
    IClock clock)
    : IQueryHandler<ListSubOrdersQuery, PagedResult<SubOrderResponse>>
{
    public async Task<Result<PagedResult<SubOrderResponse>>> HandleAsync(
        ListSubOrdersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);

        var rows = context.SubOrders
            .AsNoTracking()
            .Include(subOrder => subOrder.Lines)
            .AsQueryable();

        if (Enum.TryParse<SubOrderStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(subOrder => subOrder.Status == status);
        }

        // A vendor id from a vendor caller is ignored rather than refused: the query filter has
        // already confined them to their own, and a seller who passes somebody else's id is asking
        // for an empty page rather than committing an offence.
        if (!scope.IsVendor && query.VendorId is { } vendorId)
        {
            rows = rows.Where(subOrder => subOrder.VendorId == vendorId);
        }

        // Which shelves this queue is about. A single-warehouse deployment never passes it and sees
        // no difference; a second warehouse makes the unfiltered queue wrong for both pickers,
        // because each is shown every parcel and neither can tell which are theirs
        // (Step 28B, deliverable 12).
        if (query.WarehouseId is { } warehouseId)
        {
            rows = rows.Where(subOrder => subOrder.Lines.Any(line => line.WarehouseId == warehouseId));
        }

        if (query.OverdueOnly == true)
        {
            var now = clock.UtcNow;

            rows = rows.Where(subOrder =>
                subOrder.DispatchDueAt != null
                && subOrder.DispatchDueAt < now
                && (subOrder.Status == SubOrderStatus.Confirmed
                    || subOrder.Status == SubOrderStatus.Processing
                    || subOrder.Status == SubOrderStatus.Packed));
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(subOrder => subOrder.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(subOrder => subOrder.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var claimed = page.Take(size).ToArray();

        var invoices = await context.Invoices
            .AsNoTracking()
            .Where(invoice => claimed.Select(subOrder => subOrder.Id).Contains(invoice.SubOrderId))
            .ToDictionaryAsync(invoice => invoice.SubOrderId, cancellationToken)
            .ConfigureAwait(false);

        var items = claimed
            .Select(subOrder => OrderProjection.ToResponse(
                subOrder,
                invoices.GetValueOrDefault(subOrder.Id),
                scope.Actor))
            .ToArray();

        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<SubOrderResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>
/// Moves one seller's part along the lifecycle.
/// </summary>
/// <remarks>
/// The status arrives as a string and is parsed here rather than bound as an enum, so an unknown
/// value is a 422 that names the problem instead of a 400 from the model binder that does not.
/// </remarks>
/// <param name="context">The Ordering data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="workflow">The one place a sub-order moves.</param>
/// <param name="audit">Records the action; a transition changes what a customer is owed.</param>
internal sealed class TransitionSubOrderCommandHandler(
    OrdersDbContext context,
    OrdersScope scope,
    SubOrderWorkflow workflow,
    IAuditLogger audit) : ICommandHandler<TransitionSubOrderCommand, SubOrderResponse>
{
    public async Task<Result<SubOrderResponse>> HandleAsync(
        TransitionSubOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!Enum.TryParse<SubOrderStatus>(command.Status, ignoreCase: true, out var next))
        {
            return OrdersErrors.UnknownStatus;
        }

        var loaded = await OrderLoader
            .LoadSubOrderAsync(context, command.SubOrderId, tracked: true, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (order, subOrder) = loaded.Value;
        var from = subOrder.Status;

        var moved = await workflow
            .TransitionAsync(order, subOrder, next, scope.Actor, scope.ActorId, command.Reason, cancellationToken)
            .ConfigureAwait(false);

        if (moved.IsFailure)
        {
            return moved.Error;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = "orders.sub-order.transitioned",
                EntityType = "SubOrder",
                EntityId = subOrder.Id.ToString(),
                Before = new { Status = from.ToString() },
                After = new { Status = subOrder.Status.ToString(), command.Reason },
            },
            cancellationToken).ConfigureAwait(false);

        var invoice = await context.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.SubOrderId == subOrder.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(OrderProjection.ToResponse(subOrder, invoice, scope.Actor));
    }
}

/// <summary>Cancels one seller's part, or some units of it.</summary>
/// <param name="context">The Ordering data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="workflow">The one place a sub-order moves.</param>
/// <param name="audit">Records the action; a cancellation moves money.</param>
internal sealed class CancelSubOrderCommandHandler(
    OrdersDbContext context,
    OrdersScope scope,
    SubOrderWorkflow workflow,
    IAuditLogger audit) : ICommandHandler<CancelSubOrderCommand, SubOrderResponse>
{
    public async Task<Result<SubOrderResponse>> HandleAsync(
        CancelSubOrderCommand command,
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
        var from = subOrder.Status;

        var initiator = scope.IsVendor ? CancellationInitiator.Vendor : CancellationInitiator.Platform;

        var cancelled = await workflow
            .CancelAsync(order, subOrder, command.Lines, initiator, scope.ActorId, command.Reason, cancellationToken)
            .ConfigureAwait(false);

        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = "orders.sub-order.cancelled",
                EntityType = "SubOrder",
                EntityId = subOrder.Id.ToString(),
                Before = new { Status = from.ToString() },
                After = new
                {
                    Status = subOrder.Status.ToString(),
                    Initiator = initiator.ToString(),
                    command.Reason,
                    CancelledTotal = subOrder.CancelledTotal,
                },
            },
            cancellationToken).ConfigureAwait(false);

        var invoice = await context.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.SubOrderId == subOrder.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(OrderProjection.ToResponse(subOrder, invoice, scope.Actor));
    }
}

/// <summary>Raises a tax invoice by hand.</summary>
/// <param name="context">The Ordering data context.</param>
/// <param name="workflow">The one place an invoice is raised.</param>
/// <param name="audit">Records the action; an invoice is a statutory document.</param>
internal sealed class IssueInvoiceCommandHandler(
    OrdersDbContext context,
    SubOrderWorkflow workflow,
    IAuditLogger audit) : ICommandHandler<IssueInvoiceCommand, InvoiceResponse>
{
    public async Task<Result<InvoiceResponse>> HandleAsync(
        IssueInvoiceCommand command,
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

        var issued = await workflow.IssueInvoiceAsync(order, subOrder, cancellationToken).ConfigureAwait(false);

        if (issued.IsFailure)
        {
            return issued.Error;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = "orders.invoice.issued",
                EntityType = "Invoice",
                EntityId = issued.Value.Id.ToString(),
                After = new
                {
                    issued.Value.InvoiceNumber,
                    issued.Value.FinancialYear,
                    issued.Value.Total,
                    issued.Value.SubOrderId,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(OrderProjection.ToResponse(issued.Value));
    }
}

/// <summary>
/// Appends a note to an order's timeline.
/// </summary>
/// <remarks>
/// Internal by default. A note that a shopper can see is a message to them, and sending one by
/// accident while writing "customer sounds like a fraud risk" is exactly the mistake a default of
/// visible would eventually produce.
/// </remarks>
/// <param name="context">The Ordering data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class AddOrderNoteCommandHandler(
    OrdersDbContext context,
    OrdersScope scope,
    IClock clock) : ICommandHandler<AddOrderNoteCommand, OrderResponse>
{
    public async Task<Result<OrderResponse>> HandleAsync(
        AddOrderNoteCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await OrderLoader
            .LoadAsync(context, command.OrderId, tracked: true, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var order = loaded.Value;

        if (scope.IsVendor && order.SubOrders.Count == 0)
        {
            return OrdersErrors.NotFound("order");
        }

        order.Record(OrderEvent.Record(
            order.Id,
            subOrderId: null,
            OrderEventTypes.Note,
            scope.Actor,
            scope.ActorId,
            command.Message.Trim(),
            payload: null,
            command.IsCustomerVisible,
            clock.UtcNow));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var invoices = await OrderLoader
            .InvoicesAsync(context, order.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(OrderProjection.ToResponse(order, invoices, scope.Actor, includeInternal: true));
    }
}
