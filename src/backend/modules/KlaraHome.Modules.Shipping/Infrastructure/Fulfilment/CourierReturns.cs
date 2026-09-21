using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;

/// <summary>
/// Brings back the parcel of an order cancelled after the courier had collected it.
/// </summary>
/// <remarks>
/// <para>
/// A collected parcel cannot be cancelled with the courier, and no aggregator API recalls one in
/// transit: the only instruction a courier takes is "return to origin", and only as the answer to a
/// failed delivery attempt. So the parcel is marked when the order is cancelled, the instruction is
/// sent at once if the parcel is already at a failed attempt, and otherwise
/// <c>CourierCancellationWorker</c> sends it when the next attempt fails — which, for an order the
/// shopper no longer wants, is usually the shopper refusing it at the door.
/// </para>
/// <para>
/// Sending it closes the open failed-delivery report as a return and moves the parcel to
/// <see cref="ShipmentStatus.RtoInitiated"/>, exactly as an operator's "return to origin" does; the
/// courier's own scans take it from there to <see cref="ShipmentStatus.RtoDelivered"/>, which is
/// what releases the shopper's held refund in Payments.
/// </para>
/// <para>
/// It saves nothing except in <see cref="RetryAsync"/>; the caller commits.
/// </para>
/// </remarks>
/// <param name="context">The Shipping data context.</param>
/// <param name="providers">Finds the courier that has the parcel.</param>
/// <param name="workflow">Moves the parcel, with everything that goes with a move.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what the courier was told.</param>
internal sealed partial class CourierReturns(
    ShippingDbContext context,
    ShippingProviderRegistry providers,
    ShipmentWorkflow workflow,
    IClock clock,
    ILogger<CourierReturns> logger)
{
    /// <summary>
    /// Marks a collected parcel of a cancelled order to come back, and tells the courier if it can.
    /// </summary>
    /// <param name="shipment">The consignment, loaded for update.</param>
    /// <param name="reason">Why, for the courier's record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the parcel was one that is with the courier, and so is now marked.</returns>
    public async Task<bool> RequestAsync(Shipment shipment, string? reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        if (!IsWithCourier(shipment.Status))
        {
            return false;
        }

        shipment.RequestReturn(clock.UtcNow);
        ReturnRequested(logger, shipment.SubOrderNumber, shipment.Awb, shipment.Status);

        if (shipment.AwaitsReturnInstruction)
        {
            await SendAsync(shipment, reason, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Tells the courier to bring the parcel back, and records that it is on its way.
    /// </summary>
    /// <remarks>
    /// Public because an operator's "return to origin" on a failed-delivery report is the same
    /// instruction, and must reach the courier the same way.
    /// </remarks>
    /// <param name="shipment">The consignment, loaded for update.</param>
    /// <param name="remark">Why, for the courier's record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when the courier accepted it; the courier's refusal otherwise.</returns>
    public async Task<Result> SendAsync(Shipment shipment, string? remark, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        if (shipment.IsBooked)
        {
            var told = await providers
                .For(shipment.Provider)
                .ReturnToOriginAsync(shipment.Awb!, remark, cancellationToken)
                .ConfigureAwait(false);

            if (told.IsFailure)
            {
                CourierRefused(logger, shipment.SubOrderNumber, shipment.Awb!, told.Error.Message);
                return told;
            }
        }

        var now = clock.UtcNow;

        var open = await context.NdrRecords
            .IgnoreQueryFilters()
            .Where(record => record.ShipmentId == shipment.Id
                             && record.Action == NdrAction.Pending
                             && record.ResolvedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var record in open)
        {
            record.Decide(NdrAction.ReturnToOrigin, remark, rescheduledFor: null, actionedBy: null, now);
        }

        var scan = new CourierScan(
            $"return:{shipment.Id:N}:rto",
            ShipmentStatus.RtoInitiated,
            "Return to origin",
            Location: null,
            remark ?? "Returned to the seller: the order was cancelled.",
            NdrReason: null,
            now,
            Raw: null);

        var applied = await workflow.ApplyScanAsync(shipment, scan, cancellationToken).ConfigureAwait(false);

        if (applied.IsFailure)
        {
            return applied;
        }

        ReturnSent(logger, shipment.SubOrderNumber, shipment.Awb);

        return Result.Success();
    }

    /// <summary>Tries the instruction again for one waiting parcel, and commits what happened.</summary>
    /// <param name="shipmentId">The consignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the courier accepted it this time.</returns>
    public async Task<bool> RetryAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        var shipment = await context.Shipments
            .IgnoreQueryFilters()
            .Include(candidate => candidate.Lines)
            .FirstOrDefaultAsync(candidate => candidate.Id == shipmentId, cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null || !shipment.AwaitsReturnInstruction)
        {
            return false;
        }

        var sent = await SendAsync(shipment, "The order was cancelled.", cancellationToken).ConfigureAwait(false);

        if (sent.IsSuccess)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return sent.IsSuccess;
    }

    /// <summary>The cancelled orders' parcels now at a failed attempt, oldest request first.</summary>
    /// <param name="batchSize">How many to answer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<Guid>> PendingAsync(int batchSize, CancellationToken cancellationToken)
        => await context.Shipments
            .IgnoreQueryFilters()
            .Where(shipment => shipment.ReturnRequestedAt != null
                               && shipment.Status == ShipmentStatus.Exception)
            .OrderBy(shipment => shipment.ReturnRequestedAt)
            .Select(shipment => shipment.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Whether the courier has the parcel and has not yet delivered or returned it.</summary>
    /// <param name="status">Where the parcel is.</param>
    private static bool IsWithCourier(ShipmentStatus status)
        => status is ShipmentStatus.PickedUp
            or ShipmentStatus.InTransit
            or ShipmentStatus.OutForDelivery
            or ShipmentStatus.Exception;

    [LoggerMessage(EventId = 1774, Level = LogLevel.Warning,
        Message = "{SubOrderNumber} was cancelled with its parcel {Awb} already {Status}. It is marked to "
                  + "come back, and the courier will be told at the next failed delivery attempt.")]
    private static partial void ReturnRequested(
        ILogger logger,
        string subOrderNumber,
        string? awb,
        ShipmentStatus status);

    [LoggerMessage(EventId = 1779, Level = LogLevel.Information,
        Message = "The courier was told to return {Awb} for cancelled {SubOrderNumber}.")]
    private static partial void ReturnSent(ILogger logger, string subOrderNumber, string? awb);

    [LoggerMessage(EventId = 1789, Level = LogLevel.Warning,
        Message = "The courier did not accept the return of {Awb} for cancelled {SubOrderNumber} ({Detail}). "
                  + "It will be retried.")]
    private static partial void CourierRefused(ILogger logger, string subOrderNumber, string awb, string detail);
}
