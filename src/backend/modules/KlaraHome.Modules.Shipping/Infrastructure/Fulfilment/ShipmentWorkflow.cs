using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Events;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Shipping.Infrastructure.Fulfilment;

/// <summary>
/// Every move a consignment can make, and everything that has to happen alongside it.
/// </summary>
/// <remarks>
/// <para>
/// One place, deliberately, and for the same reason the ordering module has one. A parcel moving is
/// never just a column: dispatch tells the order it has shipped and the shopper their tracking
/// number, a failed attempt opens a report somebody has to work, delivery starts the return window
/// and turns cash at a door into cash a courier owes us. If that lived in the handlers, a webhook's
/// idea of "delivered" and an operator's would slowly stop meaning the same thing.
/// </para>
/// <para>
/// It saves nothing. Every method mutates the tracked graph and enqueues the outbox rows, and the
/// caller commits — which is what keeps a movement and its announcement in one transaction (ADR-003)
/// and what lets the webhook worker apply several scans inside one.
/// </para>
/// <para>
/// The one thing it does <em>not</em> do inside that transaction is move the order. That goes
/// through <see cref="IOrderFulfilment"/>, which commits in the ordering module's own context, and it
/// happens <b>before</b> the parcel is recorded as moved. The ordering machine is the authority on
/// whether a sub-order may become <c>Delivered</c>; asking it afterwards would mean discovering that
/// it refused once the parcel already said otherwise.
/// </para>
/// </remarks>
/// <param name="context">The Shipping data context.</param>
/// <param name="orders">The ordering seam: what a parcel is booked from, and where its movement lands.</param>
/// <param name="cash">The cash-on-delivery seam, for a parcel carrying money.</param>
/// <param name="events">Announces what happened.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what a courier moved and what it could not.</param>
internal sealed partial class ShipmentWorkflow(
    ShippingDbContext context,
    IOrderFulfilment orders,
    ICodCollections cash,
    ShippingEventPublisher events,
    IClock clock,
    ILogger<ShipmentWorkflow> logger)
{
    /// <summary>
    /// Applies one courier scan to a consignment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deduplication first: a scan already recorded against this parcel is a no-op that returns
    /// success, because a webhook and the polling fallback routinely carry the same one and neither
    /// of them is wrong to.
    /// </para>
    /// <para>
    /// A scan the machine has no edge for is <b>still recorded</b>, marked unapplied, and reported as
    /// success. It is the only trace of a courier saying something impossible — a delivery on a
    /// parcel already returned, a pickup after a cancellation — and discarding it would make that
    /// class of problem invisible. What it must not do is fail the batch: the next scan in the same
    /// payload is usually the one that makes sense.
    /// </para>
    /// </remarks>
    /// <param name="shipment">The consignment, loaded for update.</param>
    /// <param name="scan">The scan, already translated by the adapter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> ApplyScanAsync(
        Shipment shipment,
        CourierScan scan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(scan);

        var known = await context.TrackingEvents
            .IgnoreQueryFilters()
            .AnyAsync(
                entry => entry.ShipmentId == shipment.Id && entry.ProviderEventId == scan.ProviderEventId,
                cancellationToken)
            .ConfigureAwait(false);

        if (known)
        {
            shipment.MarkTracked(clock.UtcNow);
            return Result.Success();
        }

        var moved = shipment.Advance(scan.Status, scan.OccurredAt, scan.Remark ?? scan.CourierStatus);

        var entry = TrackingEvent.Record(
            shipment.Id,
            scan.ProviderEventId,
            scan.Status,
            scan.CourierStatus,
            scan.Location,
            scan.Remark,
            scan.OccurredAt,
            clock.UtcNow,
            scan.Raw,
            moved);

        context.TrackingEvents.Add(entry);
        shipment.MarkTracked(clock.UtcNow);

        if (!moved)
        {
            // Recorded and not acted on. Logged at information rather than warning: couriers repeat
            // and reorder scans constantly, and a warning per occurrence would drown the log.
            ScanNotApplied(logger, shipment.Id, shipment.Awb, scan.CourierStatus, shipment.Status);

            return Result.Success();
        }

        events.TrackingUpdated(shipment, entry);

        await AfterMoveAsync(shipment, scan, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Records that a courier has accepted a consignment, and tells the order it has shipped.
    /// </summary>
    /// <remarks>
    /// The order is moved first. Booking an air waybill against a sub-order the machine would refuse
    /// to ship — one cancelled while the packer was working — would put a parcel on a van that nobody
    /// is paying for, and the courier does not take it back for free.
    /// </remarks>
    /// <param name="shipment">The consignment, loaded for update.</param>
    /// <param name="booking">What the courier gave back.</param>
    /// <param name="provider">The adapter that booked it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> RecordBookingAsync(
        Shipment shipment,
        CourierBooking booking,
        string provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(booking);

        var now = clock.UtcNow;

        if (!shipment.Book(
                provider,
                booking.Courier,
                booking.ServiceName,
                booking.Awb,
                booking.ProviderShipmentId,
                booking.TrackingUrl,
                booking.ExpectedDeliveryAt,
                booking.FreightCost,
                now))
        {
            return Result.Failure(Application.ShippingErrors.AlreadyBooked);
        }

        // The cash record learns which parcel it is riding on, which is what turns a courier's
        // remittance file from a reconciliation exercise into a lookup.
        if (shipment.IsCod)
        {
            await cash.AttachShipmentAsync(shipment.SubOrderId, shipment.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        return Result.Success();
    }

    /// <summary>
    /// Marks a booked parcel as collected, which is the moment the order has shipped.
    /// </summary>
    /// <remarks>
    /// Separate from booking because they are separate events in the world: a parcel can carry a
    /// waybill for two days before a driver takes it, and the shopper should be told when it moves
    /// rather than when it was labelled.
    /// </remarks>
    /// <param name="shipment">The consignment, loaded for update.</param>
    /// <param name="at">When the courier took it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> RecordDispatchAsync(
        Shipment shipment,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        if (!shipment.IsBooked)
        {
            return Result.Failure(Application.ShippingErrors.NotBooked);
        }

        var told = await orders
            .AdvanceAsync(
                shipment.SubOrderId,
                "Shipped",
                $"Dispatched with {shipment.Courier}. Tracking number {shipment.Awb}.",
                cancellationToken)
            .ConfigureAwait(false);

        if (told.IsFailure)
        {
            return told;
        }

        if (!shipment.Advance(ShipmentStatus.PickedUp, at, reason: null))
        {
            return Result.Failure(Application.ShippingErrors.InvalidTransition(
                shipment.Status,
                ShipmentStatus.PickedUp));
        }

        events.Dispatched(shipment);

        return Result.Success();
    }

    /// <summary>
    /// Everything that follows a movement the machine accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is told first and the parcel's own consequences second, so a transition the ordering
    /// machine refuses stops here rather than leaving the two records disagreeing. A refusal is logged
    /// and swallowed: the scan is already recorded, the parcel has already moved, and failing the
    /// webhook would only make the courier send it again to the same refusal.
    /// </para>
    /// <para>
    /// Cash is settled on the two outcomes that decide it, and on nothing else. Delivery makes the
    /// money the courier's to remit; a return to origin means nothing was ever owed. Anything in
    /// between leaves the cash record exactly where it was.
    /// </para>
    /// </remarks>
    private async Task AfterMoveAsync(Shipment shipment, CourierScan scan, CancellationToken cancellationToken)
    {
        var status = ShipmentLifecycle.OrderStatusFor(shipment.Status);

        if (status is not null)
        {
            var told = await orders
                .AdvanceAsync(
                    shipment.SubOrderId,
                    status,
                    scan.Remark ?? ShipmentLifecycle.Narrate(shipment.Status),
                    cancellationToken)
                .ConfigureAwait(false);

            if (told.IsFailure)
            {
                OrderRefusedMovement(logger, shipment.Id, shipment.Awb, status, told.Error.Message);
            }
        }
        else
        {
            // No transition, but the shopper still wants to know their parcel reached a hub. Every
            // intermediate scan lands on the order's timeline, which is what makes it the single
            // place a support call is answered from.
            await orders
                .NoteAsync(
                    shipment.SubOrderId,
                    scan.Remark ?? ShipmentLifecycle.Narrate(shipment.Status),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        switch (shipment.Status)
        {
            case ShipmentStatus.PickedUp:
                events.Dispatched(shipment);
                break;

            case ShipmentStatus.Exception:
                await RaiseNdrAsync(shipment, scan, cancellationToken).ConfigureAwait(false);
                break;

            case ShipmentStatus.Delivered:
                await OnDeliveredAsync(shipment, cancellationToken).ConfigureAwait(false);
                break;

            case ShipmentStatus.RtoDelivered:
                await cash
                    .WaiveAsync(
                        shipment.SubOrderId,
                        "The parcel was returned to the seller undelivered.",
                        cancellationToken)
                    .ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    private async Task OnDeliveredAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        events.Delivered(shipment);

        // A failed attempt that was followed by a delivery answers its own question. Closing it here
        // is what stops an operator working a queue of parcels that already arrived.
        var open = await context.NdrRecords
            .IgnoreQueryFilters()
            .Where(record => record.ShipmentId == shipment.Id && record.Action == NdrAction.Pending)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var record in open)
        {
            record.CloseOnDelivery(shipment.DeliveredAt ?? clock.UtcNow);
        }

        if (shipment.CodAmount is { } owed and > 0m)
        {
            await cash
                .RecordCollectedAsync(
                    shipment.SubOrderId,
                    owed,
                    shipment.DeliveredAt ?? clock.UtcNow,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RaiseNdrAsync(Shipment shipment, CourierScan scan, CancellationToken cancellationToken)
    {
        // One report per attempt, and the attempt number comes from the parcel's own counter rather
        // than from the courier — they number attempts inconsistently, and a unique index on
        // (shipment, attempt) is what keeps a repeated scan from filling the queue.
        var exists = await context.NdrRecords
            .IgnoreQueryFilters()
            .AnyAsync(
                record => record.ShipmentId == shipment.Id
                          && record.AttemptNumber == shipment.DeliveryAttempts,
                cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return;
        }

        var record = NdrRecord.Raise(
            shipment,
            Math.Max(1, shipment.DeliveryAttempts),
            scan.NdrReason ?? NdrReasonCode.Other,
            scan.Remark ?? scan.CourierStatus,
            scan.OccurredAt);

        context.NdrRecords.Add(record);
        events.NdrRaised(shipment, record);
    }

    [LoggerMessage(EventId = 1740, Level = LogLevel.Information,
        Message = "A courier scan '{CourierStatus}' on parcel {ShipmentId} ({Awb}) was recorded but not "
                  + "applied: it is {Status}.")]
    private static partial void ScanNotApplied(
        ILogger logger,
        Guid shipmentId,
        string? awb,
        string? courierStatus,
        ShipmentStatus status);

    [LoggerMessage(EventId = 1741, Level = LogLevel.Warning,
        Message = "Parcel {ShipmentId} ({Awb}) moved but the order refused to become {Status}: {Detail}. "
                  + "The scan is recorded; the order is unchanged.")]
    private static partial void OrderRefusedMovement(
        ILogger logger,
        Guid shipmentId,
        string? awb,
        string status,
        string detail);
}
