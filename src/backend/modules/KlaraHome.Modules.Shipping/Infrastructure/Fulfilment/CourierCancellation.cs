using KlaraHome.Contracts.Orders;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;

/// <summary>What became of one attempt to withdraw a parcel whose order was cancelled.</summary>
internal enum CourierWithdrawal
{
    /// <summary>The parcel is cancelled, and the courier knows if it had been booked.</summary>
    Withdrawn,

    /// <summary>The courier could not be told. The parcel stays booked and the sweep will retry.</summary>
    Pending,

    /// <summary>The parcel has already left, or was already cancelled. Nothing was changed.</summary>
    NotWithdrawable,
}

/// <summary>
/// Withdraws a parcel whose order was cancelled, telling the courier first.
/// </summary>
/// <remarks>
/// <para>
/// A parcel can be booked — waybill issued, pickup scheduled — while its order is still only
/// <c>Packed</c>, because the order ships when the courier collects rather than when it is labelled.
/// A shopper may cancel in that window, and a cancellation that only changed our own record would
/// leave the courier expecting a parcel: a driver at the seller's door and a freight charge for a
/// consignment that never moved.
/// </para>
/// <para>
/// So the courier is told first, as the admin's own "cancel parcel" does. When the courier cannot be
/// reached the parcel is left in its booked state and marked, and <c>CourierCancellationWorker</c>
/// retries it. It never throws for a courier failure: it runs inside the order-cancellation event,
/// whose other handlers include the refund, and one failing handler redelivers them all.
/// </para>
/// <para>
/// It saves nothing; the caller commits.
/// </para>
/// </remarks>
/// <param name="context">The Shipping data context.</param>
/// <param name="providers">Finds the courier that holds the booking.</param>
/// <param name="packing">Reopens a parcel for what a partial cancellation left, after a retry.</param>
/// <param name="returns">Brings the parcel back when the courier collected it before cancelling.</param>
/// <param name="orders">Says whether the whole order was cancelled, over the contract.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what the courier said.</param>
internal sealed partial class CourierCancellation(
    ShippingDbContext context,
    ShippingProviderRegistry providers,
    PackingQueue packing,
    CourierReturns returns,
    IOrderFulfilment orders,
    IClock clock,
    ILogger<CourierCancellation> logger)
{
    /// <summary>Withdraws the parcel, telling its courier first when it has one.</summary>
    /// <param name="shipment">The consignment, loaded for update.</param>
    /// <param name="reason">Why, for the parcel's status reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CourierWithdrawal> WithdrawAsync(
        Shipment shipment,
        string? reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        var now = clock.UtcNow;

        if (!ShipmentLifecycle.IsTransitionAllowed(shipment.Status, ShipmentStatus.Cancelled))
        {
            if (shipment.AwaitsCourierCancellation)
            {
                // The courier collected it while we were still trying to call it off. It can only
                // come back as a return now; RetryAsync arranges one when the whole order is off.
                shipment.AbandonCourierCancellation();
                LeftBeforeCancelled(logger, shipment.SubOrderNumber, shipment.Awb, shipment.Status);
            }

            return CourierWithdrawal.NotWithdrawable;
        }

        if (shipment.IsBooked)
        {
            var told = await providers
                .For(shipment.Provider)
                .CancelShipmentAsync(shipment.ProviderShipmentId, shipment.Awb!, cancellationToken)
                .ConfigureAwait(false);

            if (told.IsFailure)
            {
                shipment.AwaitCourierCancellation(now);
                CourierRefused(logger, shipment.SubOrderNumber, shipment.Awb!, told.Error.Message);
                return CourierWithdrawal.Pending;
            }

            CourierCancelled(logger, shipment.SubOrderNumber, shipment.Awb!);
        }

        return shipment.Advance(ShipmentStatus.Cancelled, now, reason)
            ? CourierWithdrawal.Withdrawn
            : CourierWithdrawal.NotWithdrawable;
    }

    /// <summary>Offers one waiting parcel to its courier again, and commits what happened.</summary>
    /// <param name="shipmentId">The consignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CourierWithdrawal> RetryAsync(Guid shipmentId, CancellationToken cancellationToken)
    {
        var shipment = await context.Shipments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == shipmentId, cancellationToken)
            .ConfigureAwait(false);

        if (shipment is null || !shipment.AwaitsCourierCancellation)
        {
            return CourierWithdrawal.NotWithdrawable;
        }

        var wasWaiting = shipment.AwaitsCourierCancellation;

        var outcome = await WithdrawAsync(shipment, "The order was cancelled.", cancellationToken)
            .ConfigureAwait(false);

        // Collected first. When the whole order was cancelled, everything in the parcel is unwanted
        // and it comes back; after a partial cancellation it also carries units still owed, and
        // sending those back is a decision for a person, which the error logged above asks for.
        if (outcome == CourierWithdrawal.NotWithdrawable && wasWaiting)
        {
            var view = await orders.GetAsync(shipment.SubOrderId, cancellationToken).ConfigureAwait(false);

            if (view.IsSuccess && view.Value.Status == "Cancelled")
            {
                await returns.RequestAsync(shipment, "The order was cancelled.", cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // A partial cancellation whose courier was down could not reopen the rest of the order while
        // this parcel still counted as packed. It no longer does. A full cancellation finds nothing
        // left to send and opens nothing.
        if (outcome == CourierWithdrawal.Withdrawn
            && await packing.ReopenAsync(shipment.SubOrderId, cancellationToken).ConfigureAwait(false))
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return outcome;
    }

    /// <summary>The parcels still waiting on their courier, oldest first.</summary>
    /// <param name="batchSize">How many to answer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<Guid>> PendingAsync(int batchSize, CancellationToken cancellationToken)
        => await context.Shipments
            .IgnoreQueryFilters()
            .Where(shipment => shipment.CourierCancellationRequestedAt != null
                               && shipment.Status != ShipmentStatus.Cancelled)
            .OrderBy(shipment => shipment.CourierCancellationRequestedAt)
            .Select(shipment => shipment.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    [LoggerMessage(EventId = 1775, Level = LogLevel.Information,
        Message = "The courier cancelled {Awb} for {SubOrderNumber}.")]
    private static partial void CourierCancelled(ILogger logger, string subOrderNumber, string awb);

    [LoggerMessage(EventId = 1776, Level = LogLevel.Warning,
        Message = "The courier did not cancel {Awb} for cancelled {SubOrderNumber} ({Detail}). "
                  + "The parcel stays booked and the cancellation will be retried.")]
    private static partial void CourierRefused(ILogger logger, string subOrderNumber, string awb, string detail);

    [LoggerMessage(EventId = 1777, Level = LogLevel.Error,
        Message = "{SubOrderNumber} was cancelled but its parcel {Awb} reached {Status} before the courier "
                  + "accepted the cancellation. A fully cancelled order's parcel is marked to come back; "
                  + "after a partial cancellation somebody has to decide what happens to it.")]
    private static partial void LeftBeforeCancelled(
        ILogger logger,
        string subOrderNumber,
        string? awb,
        ShipmentStatus status);
}
