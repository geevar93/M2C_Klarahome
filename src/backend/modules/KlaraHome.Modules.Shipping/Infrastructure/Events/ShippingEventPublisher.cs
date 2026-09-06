using KlaraHome.Contracts.Shipping;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Shipping.Infrastructure.Events;

/// <summary>
/// Announces what happened to a parcel (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// The outbox is resolved <b>keyed by this module's context</b>, and that is not decoration. The
/// unkeyed registration is first-wins and belongs to whichever module registered first; enqueuing
/// through it here would add the row to a different context's change tracker, this module's
/// <c>SaveChangesAsync</c> would not write it, and the event would be lost with no error anywhere.
/// </para>
/// <para>
/// Nothing is saved here. The event becomes real when the caller's transaction commits, and not
/// before (ADR-003) — so no shopper is ever sent a tracking link for a dispatch that then rolled
/// back.
/// </para>
/// <para>
/// Every event here is published <em>after</em> the ordering module has already been told through
/// <c>IOrderFulfilment</c>, never instead of it. These are for the consumers that only want to know
/// it happened; the order's own movement is synchronous, because an AWB must not be booked against a
/// sub-order that would not accept the transition.
/// </para>
/// </remarks>
/// <param name="outbox">This module's outbox, keyed by its context.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ShippingEventPublisher(
    [FromKeyedServices(typeof(ShippingDbContext))] IOutbox outbox,
    IClock clock)
{
    /// <summary>A parcel was handed to a courier.</summary>
    /// <param name="shipment">The consignment, already booked.</param>
    public void Dispatched(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        outbox.Enqueue(new ShipmentDispatched(
            shipment.Id,
            shipment.OrderId,
            shipment.OrderNumber,
            shipment.SubOrderId,
            shipment.VendorId ?? Guid.Empty,
            shipment.CustomerId,
            shipment.Courier ?? shipment.Provider,
            shipment.Awb ?? string.Empty,
            shipment.TrackingUrl,
            shipment.ExpectedDeliveryAt,
            shipment.CodAmount,
            shipment.CurrencyCode,
            shipment.PickedUpAt ?? clock.UtcNow));
    }

    /// <summary>A courier reported movement.</summary>
    /// <param name="shipment">The consignment.</param>
    /// <param name="entry">The scan that was accepted.</param>
    public void TrackingUpdated(Shipment shipment, TrackingEvent entry)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(entry);

        outbox.Enqueue(new ShipmentTrackingUpdated(
            shipment.Id,
            shipment.OrderId,
            shipment.SubOrderId,
            shipment.CustomerId,
            shipment.Awb ?? string.Empty,
            entry.Status.ToString(),
            entry.CourierStatus,
            entry.Location,
            entry.Remark,
            entry.OccurredAt));
    }

    /// <summary>A parcel reached the shopper.</summary>
    /// <param name="shipment">The consignment, already delivered.</param>
    public void Delivered(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        outbox.Enqueue(new ShipmentDelivered(
            shipment.Id,
            shipment.OrderId,
            shipment.OrderNumber,
            shipment.SubOrderId,
            shipment.VendorId ?? Guid.Empty,
            shipment.CustomerId,
            shipment.Awb ?? string.Empty,
            shipment.CodAmount,
            shipment.CurrencyCode,
            shipment.DeliveredAt ?? clock.UtcNow));
    }

    /// <summary>A delivery attempt failed and somebody has to decide what happens next.</summary>
    /// <param name="shipment">The consignment.</param>
    /// <param name="record">The report that was raised.</param>
    public void NdrRaised(Shipment shipment, NdrRecord record)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(record);

        outbox.Enqueue(new ShipmentNdrRaised(
            record.Id,
            shipment.Id,
            shipment.OrderId,
            shipment.OrderNumber,
            shipment.SubOrderId,
            shipment.CustomerId,
            shipment.Awb ?? string.Empty,
            record.Reason,
            record.AttemptNumber,
            record.RaisedAt));
    }
}
