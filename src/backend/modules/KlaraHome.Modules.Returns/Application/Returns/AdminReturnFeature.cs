using FluentValidation;
using KlaraHome.Contracts.Orders;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.Modules.Returns.Infrastructure;
using KlaraHome.Modules.Returns.Infrastructure.Events;
using KlaraHome.Modules.Returns.Infrastructure.Persistence;
using KlaraHome.Modules.Returns.Infrastructure.Policy;
using KlaraHome.Modules.Returns.Infrastructure.Processing;
using KlaraHome.Modules.Returns.Infrastructure.Refunds;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Returns.Application.Returns;

/// <summary>One line an inspector graded, as the API states it.</summary>
/// <param name="ReturnLineId">The return line.</param>
/// <param name="QuantityAccepted">How many units passed.</param>
/// <param name="Disposition">What becomes of them, or null for the store's default.</param>
/// <param name="Note">What the inspector wrote about this line.</param>
internal sealed record QcLineRequest(
    Guid ReturnLineId,
    int QuantityAccepted,
    ReturnDisposition? Disposition,
    string? Note);

/// <summary>The returns queue.</summary>
/// <param name="Status">Filter by where they stand.</param>
/// <param name="VendorId">Filter to one seller. Ignored for a seller caller, who has only their own.</param>
/// <param name="OrderId">Filter to one order.</param>
/// <param name="From">Only returns raised on or after this instant.</param>
/// <param name="To">Only returns raised strictly before this instant.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListReturnsQuery(
    string? Status,
    Guid? VendorId,
    Guid? OrderId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Size) : IQuery<PagedResult<ReturnSummaryResponse>>;

/// <summary>One return in full.</summary>
/// <param name="ReturnId">The RMA.</param>
internal sealed record GetReturnQuery(Guid ReturnId) : IQuery<ReturnResponse>;

/// <summary>Agrees to a return.</summary>
/// <param name="ReturnId">The RMA.</param>
/// <param name="Amount">What to agree to, or null for everything the shopper was quoted.</param>
/// <param name="PickupRequired">Whether a courier collects, or null for what the reason says.</param>
/// <param name="Note">What the shopper should be told.</param>
internal sealed record ApproveReturnCommand(
    Guid ReturnId,
    decimal? Amount,
    bool? PickupRequired,
    string? Note) : ICommand<ReturnResponse>;

/// <summary>Refuses a return.</summary>
/// <param name="ReturnId">The RMA.</param>
/// <param name="Reason">Why, in words the shopper is shown.</param>
internal sealed record RejectReturnCommand(Guid ReturnId, string? Reason) : ICommand<ReturnResponse>;

/// <summary>Books a courier to collect an approved return.</summary>
/// <param name="ReturnId">The RMA.</param>
/// <param name="PickupAt">When the courier should call, or null for the earliest they will.</param>
/// <param name="ManualAwb">A waybill an operator obtained from a courier themselves.</param>
/// <param name="ManualCourier">Who is carrying it, for a hand-booking.</param>
internal sealed record SchedulePickupCommand(
    Guid ReturnId,
    DateTimeOffset? PickupAt,
    string? ManualAwb,
    string? ManualCourier) : ICommand<ReturnResponse>;

/// <summary>Books a parcel in at the warehouse.</summary>
/// <param name="ReturnId">The RMA.</param>
/// <param name="Note">Anything the receiving bay wants recorded.</param>
internal sealed record ReceiveReturnCommand(Guid ReturnId, string? Note) : ICommand<ReturnResponse>;

/// <summary>Records what quality control decided.</summary>
/// <param name="ReturnId">The RMA.</param>
/// <param name="Passed">Whether the goods were as they should have been.</param>
/// <param name="Disposition">What becomes of them, or null for the store's default.</param>
/// <param name="Notes">What the inspector wrote overall.</param>
/// <param name="Lines">What they concluded per line, or empty to accept everything.</param>
internal sealed record InspectReturnCommand(
    Guid ReturnId,
    bool Passed,
    ReturnDisposition? Disposition,
    string? Notes,
    IReadOnlyList<QcLineRequest>? Lines) : ICommand<ReturnResponse>;

/// <summary>Pays a return out.</summary>
/// <param name="ReturnId">The RMA.</param>
/// <param name="Mode">Where the money goes, or null for the store's default.</param>
/// <param name="Amount">What to send back, or null for everything it is worth.</param>
internal sealed record RefundReturnCommand(Guid ReturnId, string? Mode, decimal? Amount)
    : ICommand<ReturnResponse>;

/// <summary>Records that a replacement was dispatched instead of a refund.</summary>
/// <param name="ReturnId">The RMA.</param>
/// <param name="ReplacementOrderId">The order the replacement went out on, when there is one.</param>
/// <param name="Note">What the shopper should be told.</param>
internal sealed record ReplaceReturnCommand(Guid ReturnId, Guid? ReplacementOrderId, string? Note)
    : ICommand<ReturnResponse>;

/// <summary>Closes a return with nothing owed.</summary>
/// <param name="ReturnId">The RMA.</param>
/// <param name="Note">Why it is being closed.</param>
internal sealed record CloseReturnCommand(Guid ReturnId, string? Note) : ICommand<ReturnResponse>;

/// <summary>Validates an approval.</summary>
internal sealed class ApproveReturnValidator : AbstractValidator<ApproveReturnCommand>
{
    public ApproveReturnValidator()
    {
        RuleFor(command => command.Amount).GreaterThanOrEqualTo(0m).When(command => command.Amount is not null);
        RuleFor(command => command.Note).MaximumLength(500);
    }
}

/// <summary>Validates a refusal.</summary>
internal sealed class RejectReturnValidator : AbstractValidator<RejectReturnCommand>
{
    public RejectReturnValidator()
        => RuleFor(command => command.Reason).MaximumLength(500);
}

/// <summary>Validates a collection booking.</summary>
internal sealed class SchedulePickupValidator : AbstractValidator<SchedulePickupCommand>
{
    public SchedulePickupValidator()
    {
        RuleFor(command => command.ManualAwb).MaximumLength(64);
        RuleFor(command => command.ManualCourier).MaximumLength(64);
    }
}

/// <summary>Validates an inspection.</summary>
internal sealed class InspectReturnValidator : AbstractValidator<InspectReturnCommand>
{
    public InspectReturnValidator()
    {
        RuleFor(command => command.Notes).MaximumLength(2000);

        RuleForEach(command => command.Lines!).ChildRules(line =>
        {
            line.RuleFor(request => request.ReturnLineId).NotEmpty();
            line.RuleFor(request => request.QuantityAccepted).GreaterThanOrEqualTo(0);
            line.RuleFor(request => request.Note).MaximumLength(500);
        }).When(command => command.Lines is not null);
    }
}

/// <summary>Validates a payout.</summary>
internal sealed class RefundReturnValidator : AbstractValidator<RefundReturnCommand>
{
    public RefundReturnValidator()
        => RuleFor(command => command.Amount)
            .GreaterThan(0m)
            .When(command => command.Amount is not null);
}

/// <summary>
/// Loads a return for a caller who may act on it.
/// </summary>
/// <remarks>
/// One place, because every write in this file starts the same way and the vendor confinement must
/// not be something a handler can forget. The query filter already confines a seller to their own
/// rows; this makes the refusal an explicit 404 rather than a null nobody explained.
/// </remarks>
/// <param name="context">The Returns data context.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class ReturnLoader(ReturnsDbContext context, ReturnsScope scope)
{
    /// <summary>Loads it tracked, ready to be moved.</summary>
    /// <param name="returnId">The RMA.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<ReturnRequest>> LoadAsync(Guid returnId, CancellationToken cancellationToken)
    {
        var request = await context.Returns
            .FirstOrDefaultAsync(candidate => candidate.Id == returnId, cancellationToken)
            .ConfigureAwait(false);

        if (request is null)
        {
            return Result.Failure<ReturnRequest>(ReturnsErrors.NotFound("return"));
        }

        return scope.IsVendor && request.VendorId != scope.VendorId
            ? Result.Failure<ReturnRequest>(ReturnsErrors.NotFound("return"))
            : Result.Success(request);
    }
}

/// <summary>Lists the returns queue, newest first.</summary>
/// <param name="context">The Returns data context.</param>
/// <param name="scope">Who is asking; a seller sees only their own.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListReturnsQueryHandler(
    ReturnsDbContext context,
    ReturnsScope scope,
    IOptions<ReturnsOptions> options)
    : IQueryHandler<ListReturnsQuery, PagedResult<ReturnSummaryResponse>>
{
    public async Task<Result<PagedResult<ReturnSummaryResponse>>> HandleAsync(
        ListReturnsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);
        var rows = context.Returns.AsNoTracking().AsQueryable();

        if (Enum.TryParse<ReturnStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(request => request.Status == status);
        }

        // A seller's own confinement is the query filter's, not this parameter's. The parameter is
        // ignored for them rather than refused: a filter that narrowed nothing is not an error.
        if (!scope.IsVendor && query.VendorId is { } vendorId)
        {
            rows = rows.Where(request => request.VendorId == vendorId);
        }

        if (query.OrderId is { } orderId)
        {
            rows = rows.Where(request => request.OrderId == orderId);
        }

        if (query.From is { } from)
        {
            rows = rows.Where(request => request.RequestedAt >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(request => request.RequestedAt < to);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(request => request.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(request => request.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(ReturnProjection.ToSummary).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<ReturnSummaryResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads one return in full.</summary>
/// <param name="loader">Loads it, confined to what this caller may see.</param>
/// <param name="scope">Who is asking, which decides the buttons.</param>
internal sealed class GetReturnQueryHandler(ReturnLoader loader, ReturnsScope scope)
    : IQueryHandler<GetReturnQuery, ReturnResponse>
{
    public async Task<Result<ReturnResponse>> HandleAsync(
        GetReturnQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var loaded = await loader.LoadAsync(query.ReturnId, cancellationToken).ConfigureAwait(false);

        return loaded.IsFailure
            ? Result.Failure<ReturnResponse>(loaded.Error)
            : Result.Success(ReturnProjection.ToResponse(loaded.Value, scope.Actor));
    }
}

/// <summary>
/// Agrees to a return.
/// </summary>
/// <remarks>
/// <para>
/// The amount agreed may be less than the shopper was quoted — a deduction for missing packaging, a
/// negotiated settlement — and may never be more: this module quoted the figure and cannot afterwards
/// decide the goods were worth more than the shopper paid for them.
/// </para>
/// <para>
/// Whether a courier is sent is settled here rather than at the collection step, because it is part
/// of what the shopper is being told: "send it back" and "keep it, we will refund you" are different
/// answers, and both are approvals.
/// </para>
/// </remarks>
/// <param name="context">The Returns data context.</param>
/// <param name="loader">Loads it, confined to what this caller may act on.</param>
/// <param name="workflow">Moves it.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class ApproveReturnCommandHandler(
    ReturnsDbContext context,
    ReturnLoader loader,
    ReturnWorkflow workflow,
    ReturnsScope scope) : ICommandHandler<ApproveReturnCommand, ReturnResponse>
{
    public async Task<Result<ReturnResponse>> HandleAsync(
        ApproveReturnCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await loader.LoadAsync(command.ReturnId, cancellationToken).ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return Result.Failure<ReturnResponse>(loaded.Error);
        }

        var request = loaded.Value;
        var amount = command.Amount ?? request.EstimatedRefund;

        if (amount > request.EstimatedRefund)
        {
            return Result.Failure<ReturnResponse>(ReturnsErrors.RefundTooLarge(request.EstimatedRefund));
        }

        var pickup = command.PickupRequired ?? await PickupRequiredAsync(request, cancellationToken)
            .ConfigureAwait(false);

        request.Agree(amount, pickup);

        var moved = await workflow
            .TransitionAsync(
                request,
                ReturnStatus.Approved,
                scope.Actor,
                scope.ActorId,
                command.Note,
                cancellationToken)
            .ConfigureAwait(false);

        if (moved.IsFailure)
        {
            return Result.Failure<ReturnResponse>(moved.Error);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReturnProjection.ToResponse(request, scope.Actor));
    }

    /// <summary>Whether the reason says a courier has to collect.</summary>
    private async Task<bool> PickupRequiredAsync(ReturnRequest request, CancellationToken cancellationToken)
    {
        var reason = await context.Reasons
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Code == request.ReasonCode, cancellationToken)
            .ConfigureAwait(false);

        return reason?.IsPickupRequired ?? true;
    }
}

/// <summary>Refuses a return.</summary>
/// <param name="context">The Returns data context.</param>
/// <param name="loader">Loads it, confined to what this caller may act on.</param>
/// <param name="workflow">Moves it.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class RejectReturnCommandHandler(
    ReturnsDbContext context,
    ReturnLoader loader,
    ReturnWorkflow workflow,
    ReturnsScope scope) : ICommandHandler<RejectReturnCommand, ReturnResponse>
{
    public async Task<Result<ReturnResponse>> HandleAsync(
        RejectReturnCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await loader.LoadAsync(command.ReturnId, cancellationToken).ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return Result.Failure<ReturnResponse>(loaded.Error);
        }

        var request = loaded.Value;
        request.Refuse(command.Reason);

        var moved = await workflow
            .TransitionAsync(
                request,
                ReturnStatus.Rejected,
                scope.Actor,
                scope.ActorId,
                command.Reason,
                cancellationToken)
            .ConfigureAwait(false);

        if (moved.IsFailure)
        {
            return Result.Failure<ReturnResponse>(moved.Error);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReturnProjection.ToResponse(request, scope.Actor));
    }
}

/// <summary>Books a courier to collect an approved return.</summary>
/// <param name="context">The Returns data context.</param>
/// <param name="loader">Loads it, confined to what this caller may act on.</param>
/// <param name="pickups">Asks a courier.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class SchedulePickupCommandHandler(
    ReturnsDbContext context,
    ReturnLoader loader,
    ReversePickupCoordinator pickups,
    ReturnsScope scope) : ICommandHandler<SchedulePickupCommand, ReturnResponse>
{
    public async Task<Result<ReturnResponse>> HandleAsync(
        SchedulePickupCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await loader.LoadAsync(command.ReturnId, cancellationToken).ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return Result.Failure<ReturnResponse>(loaded.Error);
        }

        var request = loaded.Value;

        var booked = await pickups
            .BookAsync(
                request,
                command.PickupAt,
                command.ManualAwb,
                command.ManualCourier,
                scope.Actor,
                scope.ActorId,
                cancellationToken)
            .ConfigureAwait(false);

        if (booked.IsFailure)
        {
            return Result.Failure<ReturnResponse>(booked.Error);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReturnProjection.ToResponse(request, scope.Actor));
    }
}

/// <summary>
/// Books a parcel in at the warehouse.
/// </summary>
/// <remarks>
/// Deliberately its own step and deliberately not the inspection. The parcel arriving and somebody
/// opening it are different days and different people, and a platform that collapsed them could not
/// answer how much is sitting in the receiving bay uninspected — which is the number a returns
/// operation is actually run on.
/// </remarks>
/// <param name="context">The Returns data context.</param>
/// <param name="loader">Loads it, confined to what this caller may act on.</param>
/// <param name="workflow">Moves it.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class ReceiveReturnCommandHandler(
    ReturnsDbContext context,
    ReturnLoader loader,
    ReturnWorkflow workflow,
    ReturnsScope scope) : ICommandHandler<ReceiveReturnCommand, ReturnResponse>
{
    public async Task<Result<ReturnResponse>> HandleAsync(
        ReceiveReturnCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await loader.LoadAsync(command.ReturnId, cancellationToken).ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return Result.Failure<ReturnResponse>(loaded.Error);
        }

        var request = loaded.Value;

        var moved = await workflow
            .TransitionAsync(
                request,
                ReturnStatus.Received,
                scope.Actor,
                scope.ActorId,
                command.Note,
                cancellationToken)
            .ConfigureAwait(false);

        if (moved.IsFailure)
        {
            return Result.Failure<ReturnResponse>(moved.Error);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReturnProjection.ToResponse(request, scope.Actor));
    }
}

/// <summary>
/// Records what quality control decided, and — where the store says so — refunds on the spot.
/// </summary>
/// <remarks>
/// The automatic refund is a setting rather than a hard-coded step, and it sits <em>on top of</em>
/// the Payments module's maker–checker threshold rather than replacing it: a large refund still
/// waits for a second pair of eyes, it simply does not also wait for somebody to click "refund"
/// after the decision has already been made.
/// </remarks>
/// <param name="context">The Returns data context.</param>
/// <param name="loader">Loads it, confined to what this caller may act on.</param>
/// <param name="orders">Reads the sale, for the tax split a credit note needs.</param>
/// <param name="inspection">Records the verdict and acts on it.</param>
/// <param name="refunds">Pays it out, when the store refunds automatically.</param>
/// <param name="policy">Supplies the default disposition and the automatic-refund switch.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class InspectReturnCommandHandler(
    ReturnsDbContext context,
    ReturnLoader loader,
    IOrderReturns orders,
    ReturnInspectionService inspection,
    ReturnRefundService refunds,
    ReturnPolicyService policy,
    ReturnsScope scope) : ICommandHandler<InspectReturnCommand, ReturnResponse>
{
    public async Task<Result<ReturnResponse>> HandleAsync(
        InspectReturnCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await loader.LoadAsync(command.ReturnId, cancellationToken).ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return Result.Failure<ReturnResponse>(loaded.Error);
        }

        var request = loaded.Value;

        if (request.Status is not (ReturnStatus.Received or ReturnStatus.QcFailed))
        {
            return Result.Failure<ReturnResponse>(ReturnsErrors.NotReceived);
        }

        var view = await orders
            .GetAsync(request.SubOrderId, customerId: null, cancellationToken)
            .ConfigureAwait(false);

        if (view.IsFailure)
        {
            return Result.Failure<ReturnResponse>(ReturnsErrors.OrderUnavailable(view.Error.Message));
        }

        var order = view.Value;
        var resolved = await policy.ResolveAsync(order.VendorId, cancellationToken).ConfigureAwait(false);

        var fallback = Chosen(command.Disposition)
                       ?? ReturnPolicyService.DefaultDisposition(command.Passed, resolved);

        var verdicts = (command.Lines ?? [])
            .Select(line => new LineVerdict(
                line.ReturnLineId,
                line.QuantityAccepted,
                Chosen(line.Disposition),
                line.Note))
            .ToArray();

        var inspected = await inspection
            .InspectAsync(
                request,
                command.Passed,
                command.Notes,
                verdicts,
                fallback,
                scope.Actor,
                scope.ActorId,
                cancellationToken)
            .ConfigureAwait(false);

        if (inspected.IsFailure)
        {
            return Result.Failure<ReturnResponse>(inspected.Error);
        }

        if (command.Passed && resolved.Store.AutoRefundOnQcPass && request.Type == ReturnType.Return)
        {
            var mode = ReturnPolicyService.RefundMode(null, resolved);

            var paid = await refunds
                .PayAsync(request, order, mode, amount: null, scope.Actor, scope.ActorId, cancellationToken)
                .ConfigureAwait(false);

            if (paid.IsFailure)
            {
                // The inspection stands whatever the money did. A gateway that refused a refund is a
                // problem an operator works from the queue; undoing the QC verdict because of it
                // would put the goods back into "not yet inspected" with the stock already moved.
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return Result.Failure<ReturnResponse>(paid.Error);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReturnProjection.ToResponse(request, scope.Actor));
    }

    /// <summary>Reads a disposition from the API's spelling of it.</summary>
    /// <summary>
    /// The disposition an inspector actually chose, or null to fall back to the store's default.
    /// </summary>
    /// <remarks>
    /// <see cref="ReturnDisposition.Pending"/> is treated as "not chosen" rather than as a choice:
    /// it is the state a line is in before anybody has graded it, and sending it back as a verdict
    /// would set a line to un-inspected while claiming to have inspected it.
    /// </remarks>
    /// <param name="value">What the caller sent.</param>
    private static ReturnDisposition? Chosen(ReturnDisposition? value)
        => value is { } chosen && chosen != ReturnDisposition.Pending ? chosen : null;
}

/// <summary>Pays a return out, when it was not paid automatically.</summary>
/// <param name="context">The Returns data context.</param>
/// <param name="loader">Loads it, confined to what this caller may act on.</param>
/// <param name="orders">Reads the sale, for the tax split a credit note needs.</param>
/// <param name="refunds">Sends the money and raises the note.</param>
/// <param name="policy">Supplies the default mode.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class RefundReturnCommandHandler(
    ReturnsDbContext context,
    ReturnLoader loader,
    IOrderReturns orders,
    ReturnRefundService refunds,
    ReturnPolicyService policy,
    ReturnsScope scope) : ICommandHandler<RefundReturnCommand, ReturnResponse>
{
    public async Task<Result<ReturnResponse>> HandleAsync(
        RefundReturnCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await loader.LoadAsync(command.ReturnId, cancellationToken).ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return Result.Failure<ReturnResponse>(loaded.Error);
        }

        var request = loaded.Value;

        var view = await orders
            .GetAsync(request.SubOrderId, customerId: null, cancellationToken)
            .ConfigureAwait(false);

        if (view.IsFailure)
        {
            return Result.Failure<ReturnResponse>(ReturnsErrors.OrderUnavailable(view.Error.Message));
        }

        var order = view.Value;
        var resolved = await policy.ResolveAsync(order.VendorId, cancellationToken).ConfigureAwait(false);
        var requested = ParseMode(command.Mode);

        if (requested == ReturnRefundMode.Wallet && !resolved.Store.AllowWalletRefunds)
        {
            return Result.Failure<ReturnResponse>(ReturnsErrors.WalletUnavailable);
        }

        var mode = ReturnPolicyService.RefundMode(requested, resolved);

        var paid = await refunds
            .PayAsync(request, order, mode, command.Amount, scope.Actor, scope.ActorId, cancellationToken)
            .ConfigureAwait(false);

        if (paid.IsFailure)
        {
            // The credit note is raised before the money is asked for, and it is raised whether or
            // not money moves — that is the whole point of issuing it first. A gateway or wallet
            // refusal must not take it back down with it: saving here is what makes a return refused
            // for want of anything to refund (RETURN_NOTHING_REFUNDABLE, a cash-on-delivery parcel
            // among them) keep the note it already earned.
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Failure<ReturnResponse>(paid.Error);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReturnProjection.ToResponse(request, scope.Actor));
    }

    /// <summary>Reads a refund mode from the API's spelling of it.</summary>
    private static ReturnRefundMode? ParseMode(string? value)
        => Enum.TryParse<ReturnRefundMode>(value, ignoreCase: true, out var parsed) ? parsed : null;
}

/// <summary>
/// Records that a replacement was dispatched instead of a refund.
/// </summary>
/// <remarks>
/// The replacement is dispatched as an ordinary order through the ordering and shipping modules —
/// this records that it happened and closes the RMA against it. Placing that order from inside this
/// module would mean a returns module that can create sales, which is a seam nothing else needs and
/// a great deal of authority to hand a returns queue.
/// </remarks>
/// <param name="context">The Returns data context.</param>
/// <param name="loader">Loads it, confined to what this caller may act on.</param>
/// <param name="workflow">Moves it.</param>
/// <param name="events">Announces the close.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class ReplaceReturnCommandHandler(
    ReturnsDbContext context,
    ReturnLoader loader,
    ReturnWorkflow workflow,
    ReturnsEventPublisher events,
    ReturnsScope scope) : ICommandHandler<ReplaceReturnCommand, ReturnResponse>
{
    public async Task<Result<ReturnResponse>> HandleAsync(
        ReplaceReturnCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await loader.LoadAsync(command.ReturnId, cancellationToken).ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return Result.Failure<ReturnResponse>(loaded.Error);
        }

        var request = loaded.Value;

        if (request.Type != ReturnType.Replacement)
        {
            return Result.Failure<ReturnResponse>(ReturnsErrors.ReplacementUnavailable);
        }

        if (command.ReplacementOrderId is { } orderId)
        {
            request.AttachReplacement(orderId);
        }

        var moved = await workflow
            .TransitionAsync(
                request,
                ReturnStatus.Replaced,
                scope.Actor,
                scope.ActorId,
                command.Note,
                cancellationToken)
            .ConfigureAwait(false);

        if (moved.IsFailure)
        {
            return Result.Failure<ReturnResponse>(moved.Error);
        }

        var closed = await workflow
            .TransitionAsync(
                request,
                ReturnStatus.Closed,
                scope.Actor,
                scope.ActorId,
                note: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (closed.IsSuccess)
        {
            events.Closed(request, ReturnOutcomes.Replaced);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReturnProjection.ToResponse(request, scope.Actor));
    }
}

/// <summary>Closes a return with nothing owed.</summary>
/// <remarks>
/// The end of the road for a return that failed inspection and was not argued, and for goods nobody
/// claimed. It is the platform's alone: a seller closing a return they are about to be charged for
/// would be deciding their own liability.
/// </remarks>
/// <param name="context">The Returns data context.</param>
/// <param name="loader">Loads it, confined to what this caller may act on.</param>
/// <param name="workflow">Moves it.</param>
/// <param name="events">Announces the close.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class CloseReturnCommandHandler(
    ReturnsDbContext context,
    ReturnLoader loader,
    ReturnWorkflow workflow,
    ReturnsEventPublisher events,
    ReturnsScope scope) : ICommandHandler<CloseReturnCommand, ReturnResponse>
{
    public async Task<Result<ReturnResponse>> HandleAsync(
        CloseReturnCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await loader.LoadAsync(command.ReturnId, cancellationToken).ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return Result.Failure<ReturnResponse>(loaded.Error);
        }

        var request = loaded.Value;

        var moved = await workflow
            .TransitionAsync(
                request,
                ReturnStatus.Closed,
                scope.Actor,
                scope.ActorId,
                command.Note,
                cancellationToken)
            .ConfigureAwait(false);

        if (moved.IsFailure)
        {
            return Result.Failure<ReturnResponse>(moved.Error);
        }

        events.Closed(request, ReturnOutcomes.Closed);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReturnProjection.ToResponse(request, scope.Actor));
    }
}
