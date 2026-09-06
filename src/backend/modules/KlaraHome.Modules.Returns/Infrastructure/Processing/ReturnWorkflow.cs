using KlaraHome.Contracts.Orders;
using KlaraHome.Modules.Returns.Application;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.Modules.Returns.Infrastructure.Events;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Returns.Infrastructure.Processing;

/// <summary>
/// The single place a return moves.
/// </summary>
/// <remarks>
/// <para>
/// Every route into a state change goes through here: a shopper cancelling, an operator approving, a
/// courier scan arriving over the shipping events, and the sweeper chasing a collection that never
/// happened. That is what stops the four drifting about what "received" means and what each one owes
/// the order timeline.
/// </para>
/// <para>
/// It does three things in a fixed order and never any of them alone. It asks the transition table
/// whether the edge exists for this actor; it writes the corresponding line on the <em>order's</em>
/// timeline rather than on a timeline of its own, because a support call is answered from one place
/// (docs/04-api-specification.md §3.4); and it moves the sub-order when the return's state means
/// something to the sale. A failure of the last two does not undo the first — an order module that
/// is briefly unavailable must not leave a return that everybody has been told was approved sitting
/// in <c>Requested</c>.
/// </para>
/// <para>
/// It does not save. The caller owns the transaction, which is what lets an approval write the
/// transition, the freight decision and the outbox message in one unit.
/// </para>
/// </remarks>
/// <param name="orders">The seam the sale is moved and annotated through.</param>
/// <param name="events">Announces what happened.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports the moves and the refusals.</param>
internal sealed partial class ReturnWorkflow(
    IOrderReturns orders,
    ReturnsEventPublisher events,
    IClock clock,
    ILogger<ReturnWorkflow> logger)
{
    /// <summary>
    /// Moves a return, if the machine has the edge and this actor may take it.
    /// </summary>
    /// <param name="request">The RMA.</param>
    /// <param name="next">Where it is going.</param>
    /// <param name="actor">Who is asking.</param>
    /// <param name="actorId">The user, when there is one.</param>
    /// <param name="note">What the order timeline should say, or null for the machine's own words.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> TransitionAsync(
        ReturnRequest request,
        ReturnStatus next,
        ReturnActor actor,
        Guid? actorId,
        string? note,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Status == next)
        {
            // Idempotent by design. A redelivered courier scan and an operator clicking twice both
            // land here, and both are successes: the return is where the caller wanted it.
            return Result.Success();
        }

        if (request.IsTerminal)
        {
            return Result.Failure(ReturnsErrors.AlreadyClosed(request.Status));
        }

        var from = request.Status;

        if (!request.Transition(next, actor, actorId, clock.UtcNow))
        {
            NotMoved(logger, request.ReturnNumber, from, next, actor);

            return Result.Failure(
                ReturnLifecycle.Exists(from, next)
                    ? ReturnsErrors.NotYours
                    : ReturnsErrors.InvalidTransition(from, next));
        }

        await AnnotateAsync(request, next, note, cancellationToken).ConfigureAwait(false);
        await SyncOrderAsync(request, next, cancellationToken).ConfigureAwait(false);

        Publish(request, next);

        Moved(logger, request.ReturnNumber, from, next);

        return Result.Success();
    }

    /// <summary>
    /// Writes a line on the order's timeline for the move.
    /// </summary>
    /// <remarks>
    /// Failure is logged and swallowed. A timeline entry is worth having and is not worth failing an
    /// approval for: a shopper whose return was approved and whose order history does not say so has
    /// a cosmetic problem, and one whose approval was refused because a note could not be written
    /// has a real one.
    /// </remarks>
    private async Task AnnotateAsync(
        ReturnRequest request,
        ReturnStatus next,
        string? note,
        CancellationToken cancellationToken)
    {
        var text = note is { Length: > 0 } ? note : ReturnLifecycle.Narrate(next);
        var line = $"{text} ({request.ReturnNumber})";

        var written = await orders
            .NoteAsync(request.SubOrderId, line, cancellationToken)
            .ConfigureAwait(false);

        if (written.IsFailure)
        {
            TimelineUnavailable(logger, request.ReturnNumber, written.Error.Message);
        }
    }

    /// <summary>
    /// Moves the sub-order, when the return's new state means something to the sale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only three states cross the boundary, and the mapping lives in the lifecycle rather than
    /// here. A refusal from the ordering machine is logged rather than propagated for the same
    /// reason a failed timeline entry is: the return has already moved, everybody has been told, and
    /// undoing it because the sale is in an unexpected state would leave the two <em>more</em>
    /// inconsistent rather than less.
    /// </para>
    /// <para>
    /// It is taken as the system on purpose. A shopper cancelling their return is not entitled to
    /// move a sub-order, and the ordering module's own actor table is what says so.
    /// </para>
    /// </remarks>
    private async Task SyncOrderAsync(
        ReturnRequest request,
        ReturnStatus next,
        CancellationToken cancellationToken)
    {
        if (ReturnLifecycle.OrderStatusFor(next) is not { } orderStatus)
        {
            return;
        }

        var moved = await orders
            .AdvanceAsync(request.SubOrderId, orderStatus, ReturnLifecycle.Narrate(next), cancellationToken)
            .ConfigureAwait(false);

        if (moved.IsFailure)
        {
            OrderNotMoved(logger, request.ReturnNumber, orderStatus, moved.Error.Message);
        }
    }

    /// <summary>Announces the states that other modules act on. The rest are timeline entries.</summary>
    private void Publish(ReturnRequest request, ReturnStatus next)
    {
        switch (next)
        {
            case ReturnStatus.Approved:
                events.Approved(request);
                break;
            case ReturnStatus.Rejected:
                events.Rejected(request);
                events.Closed(request, ReturnOutcomes.Rejected);
                break;
            case ReturnStatus.Received:
                events.Received(request);
                break;
            case ReturnStatus.Cancelled:
                events.Closed(request, ReturnOutcomes.Cancelled);
                break;
            default:
                break;
        }
    }

    [LoggerMessage(EventId = 1710, Level = LogLevel.Information,
        Message = "Return {ReturnNumber} moved from {From} to {To}.")]
    private static partial void Moved(
        ILogger logger,
        string returnNumber,
        ReturnStatus from,
        ReturnStatus to);

    [LoggerMessage(EventId = 1711, Level = LogLevel.Warning,
        Message = "Return {ReturnNumber} was not moved from {From} to {To}: {Actor} may not take that edge, "
                  + "or the machine does not have it.")]
    private static partial void NotMoved(
        ILogger logger,
        string returnNumber,
        ReturnStatus from,
        ReturnStatus to,
        ReturnActor actor);

    [LoggerMessage(EventId = 1712, Level = LogLevel.Warning,
        Message = "The order timeline could not be written for return {ReturnNumber}: {Detail}")]
    private static partial void TimelineUnavailable(ILogger logger, string returnNumber, string detail);

    [LoggerMessage(EventId = 1713, Level = LogLevel.Warning,
        Message = "Return {ReturnNumber} moved, but its sub-order could not be advanced to {Status}: {Detail}")]
    private static partial void OrderNotMoved(
        ILogger logger,
        string returnNumber,
        string status,
        string detail);
}

/// <summary>How a return ended, as the closing event reports it.</summary>
internal static class ReturnOutcomes
{
    /// <summary>The money went back.</summary>
    public const string Refunded = "Refunded";

    /// <summary>A replacement was dispatched instead.</summary>
    public const string Replaced = "Replaced";

    /// <summary>It was refused.</summary>
    public const string Rejected = "Rejected";

    /// <summary>The shopper withdrew it.</summary>
    public const string Cancelled = "Cancelled";

    /// <summary>It finished with nothing owed — a failed inspection, or goods nobody claimed.</summary>
    public const string Closed = "Closed";
}
