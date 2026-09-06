using KlaraHome.Contracts.Shipping;
using KlaraHome.Modules.Returns.Application;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Returns.Infrastructure.Processing;

/// <summary>
/// Gets a courier to the shopper's door, and calls one off.
/// </summary>
/// <remarks>
/// <para>
/// A thin coordinator on purpose. Everything about how a parcel is booked, tracked and labelled
/// belongs to the Shipping module and reaches this one through <see cref="IReversePickup"/>; what is
/// left here is the decision of <em>whether</em> to book — which returns need a courier, which do
/// not, and what the return's own state should become afterwards.
/// </para>
/// <para>
/// A booking failure does not undo the approval. The shopper has been told their return is agreed
/// and the goods are theirs to hand over; a courier that could not be reached is an operator's
/// problem, and the return sits in <see cref="ReturnStatus.Approved"/> where the sweep will find it.
/// That is also the state a deployment with no logistics account lives in until somebody types in a
/// waybill by hand.
/// </para>
/// </remarks>
/// <param name="pickups">The seam a courier is asked through.</param>
/// <param name="workflow">Moves the return once a courier has agreed.</param>
/// <param name="logger">Reports what was booked and what was not.</param>
internal sealed partial class ReversePickupCoordinator(
    IReversePickup pickups,
    ReturnWorkflow workflow,
    ILogger<ReversePickupCoordinator> logger)
{
    /// <summary>
    /// Books a collection for an approved return.
    /// </summary>
    /// <param name="request">The RMA.</param>
    /// <param name="scheduledFor">When the courier should call, or null for the earliest they will.</param>
    /// <param name="manualAwb">A waybill an operator obtained from a courier themselves.</param>
    /// <param name="manualCourier">Who is carrying it, for a hand-booking.</param>
    /// <param name="actor">Who is asking.</param>
    /// <param name="actorId">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> BookAsync(
        ReturnRequest request,
        DateTimeOffset? scheduledFor,
        string? manualAwb,
        string? manualCourier,
        ReturnActor actor,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.IsPickupRequired)
        {
            // The store told the shopper to keep the goods. There is nothing to collect, and booking
            // a van would be a cost against a decision already taken not to incur one.
            return Result.Failure(ReturnsErrors.InvalidTransition(request.Status, ReturnStatus.PickupScheduled));
        }

        if (request.PickupShipmentId is not null)
        {
            // Already booked. Idempotent rather than a conflict: an operator clicking twice wants
            // the collection that exists, not a second van.
            return Result.Success();
        }

        var booked = await pickups
            .BookAsync(
                new ReversePickupRequest(
                    request.SubOrderId,
                    request.Id,
                    request.ReturnNumber,
                    [.. request.Lines.Select(line => new ReversePickupLine(line.OrderLineId, line.Quantity))],
                    scheduledFor,
                    manualAwb,
                    manualCourier),
                cancellationToken)
            .ConfigureAwait(false);

        if (booked.IsFailure)
        {
            NotBooked(logger, request.ReturnNumber, booked.Error.Message);
            return Result.Failure(ReturnsErrors.PickupFailed(booked.Error.Message));
        }

        request.Collect(booked.Value.ShipmentId, booked.Value.Awb, booked.Value.ScheduledFor);

        var note = booked.Value.ScheduledFor is { } due
            ? $"A courier will collect your return on {due:d MMMM yyyy}. Waybill {booked.Value.Awb}."
            : $"A courier has been arranged to collect your return. Waybill {booked.Value.Awb}.";

        var moved = await workflow
            .TransitionAsync(request, ReturnStatus.PickupScheduled, actor, actorId, note, cancellationToken)
            .ConfigureAwait(false);

        if (moved.IsSuccess)
        {
            Booked(logger, request.ReturnNumber, booked.Value.Awb, booked.Value.Courier ?? "manual");
        }

        return moved;
    }

    /// <summary>
    /// Calls off a collection that has not happened.
    /// </summary>
    /// <remarks>
    /// Failure is logged and swallowed. This runs when a shopper withdraws their return, and a
    /// courier booking that could not be cancelled is a wasted van — it is not a reason to refuse
    /// the shopper the cancellation they asked for.
    /// </remarks>
    /// <param name="request">The RMA.</param>
    /// <param name="reason">Why, for the parcel's own timeline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StandDownAsync(
        ReturnRequest request,
        string? reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.PickupShipmentId is not { } shipmentId)
        {
            return;
        }

        var cancelled = await pickups
            .CancelAsync(shipmentId, reason, cancellationToken)
            .ConfigureAwait(false);

        if (cancelled.IsFailure)
        {
            NotStoodDown(logger, request.ReturnNumber, cancelled.Error.Message);
            return;
        }

        request.Collect(shipmentId: null, awb: null, scheduledFor: null);

        StoodDown(logger, request.ReturnNumber);
    }

    [LoggerMessage(EventId = 1750, Level = LogLevel.Information,
        Message = "A collection was booked for return {ReturnNumber} on waybill {Awb} with {Courier}.")]
    private static partial void Booked(ILogger logger, string returnNumber, string awb, string courier);

    [LoggerMessage(EventId = 1751, Level = LogLevel.Error,
        Message = "No collection was booked for return {ReturnNumber}: {Detail}")]
    private static partial void NotBooked(ILogger logger, string returnNumber, string detail);

    [LoggerMessage(EventId = 1752, Level = LogLevel.Information,
        Message = "The collection for return {ReturnNumber} was called off.")]
    private static partial void StoodDown(ILogger logger, string returnNumber);

    [LoggerMessage(EventId = 1753, Level = LogLevel.Warning,
        Message = "The collection for return {ReturnNumber} could not be called off: {Detail}")]
    private static partial void NotStoodDown(ILogger logger, string returnNumber, string detail);
}
