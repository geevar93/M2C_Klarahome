using KlaraHome.Contracts.IntegrationEvents;

namespace KlaraHome.Contracts.Shipping;

/// <summary>
/// A parcel was handed to a courier (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// The first fact in the life of a consignment that anybody outside Shipping cares about. Orders
/// moves the seller's part to <c>Shipped</c> on it, Notifications sends the shopper their tracking
/// link, and Settlements learns the freight it will net off the seller's payout.
/// </remarks>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number, which is what a shopper quotes to support.</param>
/// <param name="SubOrderId">The seller's part being dispatched.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Courier">The courier carrying it.</param>
/// <param name="Awb">The air waybill — the number that tracks it everywhere.</param>
/// <param name="TrackingUrl">Where a shopper can watch it, when the courier publishes one.</param>
/// <param name="ExpectedDeliveryAt">The courier's promise, when it makes one.</param>
/// <param name="CodAmount">Cash to collect at the door, or null for a prepaid parcel.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="DispatchedAt">When it left.</param>
public sealed record ShipmentDispatched(
    Guid ShipmentId,
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    Guid VendorId,
    Guid CustomerId,
    string Courier,
    string Awb,
    string? TrackingUrl,
    DateTimeOffset? ExpectedDeliveryAt,
    decimal? CodAmount,
    string CurrencyCode,
    DateTimeOffset DispatchedAt) : IntegrationEvent;

/// <summary>
/// A courier reported movement on a parcel.
/// </summary>
/// <remarks>
/// <para>
/// Raised once per accepted tracking event, and deliberately not once per poll: a courier that
/// repeats itself produces one row and one event the first time and nothing afterwards, because the
/// event id is unique per shipment.
/// </para>
/// <para>
/// It carries this platform's own vocabulary rather than the courier's words. Every aggregator
/// spells "out for delivery" differently, and a consumer that had to know all of them would be a
/// consumer that breaks when the aggregator is changed.
/// </para>
/// </remarks>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="OrderId">The order.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Awb">The air waybill.</param>
/// <param name="Status">Where the parcel now is, in this platform's vocabulary.</param>
/// <param name="CourierStatus">The courier's own word for it, kept so a support call can quote it.</param>
/// <param name="Location">Where the scan happened, when the courier says.</param>
/// <param name="Remark">What the courier wrote.</param>
/// <param name="OccurredAt">When the courier says it happened.</param>
public sealed record ShipmentTrackingUpdated(
    Guid ShipmentId,
    Guid OrderId,
    Guid SubOrderId,
    Guid CustomerId,
    string Awb,
    string Status,
    string? CourierStatus,
    string? Location,
    string? Remark,
    DateTimeOffset OccurredAt) : IntegrationEvent;

/// <summary>
/// A parcel reached the shopper.
/// </summary>
/// <remarks>
/// Separate from <see cref="ShipmentTrackingUpdated"/> although delivery is also a tracking update,
/// because it is the moment the return window opens, the seller's settlement becomes earnable and
/// the cash on a COD parcel becomes the courier's to remit. A consumer that cares only about
/// delivery should not have to filter every scan to find it.
/// </remarks>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Awb">The air waybill.</param>
/// <param name="CodAmount">Cash the courier took at the door, or null for a prepaid parcel.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="DeliveredAt">When it was handed over.</param>
public sealed record ShipmentDelivered(
    Guid ShipmentId,
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    Guid VendorId,
    Guid CustomerId,
    string Awb,
    decimal? CodAmount,
    string CurrencyCode,
    DateTimeOffset DeliveredAt) : IntegrationEvent;

/// <summary>
/// A delivery attempt failed and somebody has to decide what happens next.
/// </summary>
/// <remarks>
/// The non-delivery report. It is its own event rather than a status because it is the one tracking
/// outcome that needs a human: a shopper who was out, an address nobody could find, a refusal at the
/// door. Notifications chases the shopper on it and the admin NDR queue is worked from it.
/// </remarks>
/// <param name="NdrId">The report.</param>
/// <param name="ShipmentId">The consignment.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="Awb">The air waybill.</param>
/// <param name="Reason">Why the parcel was not delivered, in the courier's words.</param>
/// <param name="AttemptNumber">Which attempt this was, counting from one.</param>
/// <param name="RaisedAt">When the attempt failed.</param>
public sealed record ShipmentNdrRaised(
    Guid NdrId,
    Guid ShipmentId,
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    Guid CustomerId,
    string Awb,
    string? Reason,
    int AttemptNumber,
    DateTimeOffset RaisedAt) : IntegrationEvent;
