using KlaraHome.Modules.Shipping.Domain;

namespace KlaraHome.Modules.Shipping.Application;

/// <summary>One PIN-code run in a zone's map.</summary>
/// <param name="From">The first PIN code, inclusive.</param>
/// <param name="To">The last PIN code, inclusive.</param>
internal sealed record PincodeRangeModel(string From, string To);

/// <summary>A delivery region.</summary>
/// <param name="Id">The zone.</param>
/// <param name="Code">Its stable code, which rate rules refer to.</param>
/// <param name="Name">What it is called.</param>
/// <param name="States">The states it covers.</param>
/// <param name="PincodeRanges">The PIN-code runs it covers.</param>
/// <param name="Priority">Lower wins where two zones both match.</param>
/// <param name="IsActive">Whether it is used when a rate is looked up.</param>
/// <param name="IsCatchAll">Whether it covers nowhere in particular, and therefore everywhere.</param>
internal sealed record ShippingZoneResponse(
    Guid Id,
    string Code,
    string Name,
    IReadOnlyList<Guid> States,
    IReadOnlyList<PincodeRangeModel> PincodeRanges,
    int Priority,
    bool IsActive,
    bool IsCatchAll);

/// <summary>One rule of the rate card.</summary>
/// <param name="Id">The rule.</param>
/// <param name="ZoneId">The zone it prices.</param>
/// <param name="VendorId">The seller it overrides for, or null for the platform's card.</param>
/// <param name="Method">Standard or express.</param>
/// <param name="MinWeightGrams">The lightest parcel it applies to.</param>
/// <param name="MaxWeightGrams">The heaviest.</param>
/// <param name="MinOrderValue">The smallest basket it applies to.</param>
/// <param name="MaxOrderValue">The largest, or null for none.</param>
/// <param name="BaseRate">The band's floor price, inclusive of tax.</param>
/// <param name="PerKgRate">What each further kilogram adds.</param>
/// <param name="FreeAbove">The basket value at or above which delivery is free.</param>
/// <param name="CodFee">What cash on delivery adds.</param>
/// <param name="IsCodAllowed">Whether cash on delivery may be chosen on this service.</param>
/// <param name="CurrencyCode">ISO 4217 code every amount is in.</param>
/// <param name="EtaMinDays">The earliest delivery, in days from dispatch.</param>
/// <param name="EtaMaxDays">The latest.</param>
/// <param name="IsActive">Whether the rule is used.</param>
internal sealed record ShippingRateResponse(
    Guid Id,
    Guid ZoneId,
    Guid? VendorId,
    string Method,
    int MinWeightGrams,
    int MaxWeightGrams,
    decimal MinOrderValue,
    decimal? MaxOrderValue,
    decimal BaseRate,
    decimal PerKgRate,
    decimal? FreeAbove,
    decimal CodFee,
    bool IsCodAllowed,
    string CurrencyCode,
    int EtaMinDays,
    int EtaMaxDays,
    bool IsActive);

/// <summary>
/// What can be delivered to one PIN code (docs/04-api-specification.md §3.8).
/// </summary>
/// <remarks>
/// It answers both halves of the question and says which one failed (ADR-018).
/// <see cref="Deliverable"/> is <see cref="Covered"/> and <see cref="IsServiceable"/> together, and
/// is the only field a storefront needs to decide what to show; <see cref="Reason"/> and
/// <see cref="Message"/> are what it says when the answer is no.
/// </remarks>
/// <param name="Pincode">The six-digit destination.</param>
/// <param name="Deliverable">Whether an order to it may be placed at all.</param>
/// <param name="Covered">Whether this store's delivery area includes it.</param>
/// <param name="IsServiceable">Whether any courier will carry a parcel there.</param>
/// <param name="PrepaidOk">Whether a prepaid parcel can be.</param>
/// <param name="CodOk">Whether cash can be collected there.</param>
/// <param name="EtaDays">How long a courier says it takes, when one says.</param>
/// <param name="Courier">Whose answer this is.</param>
/// <param name="City">The city, from the platform's reference data or the courier's answer.</param>
/// <param name="State">The state, likewise.</param>
/// <param name="Reason">Which check refused it: <c>DELIVERY_AREA_NOT_COVERED</c>, <c>PINCODE_NOT_SERVICEABLE</c>, or null.</param>
/// <param name="Message">The operator's own words for an out-of-area destination.</param>
/// <param name="CheckedAt">When the answer was obtained. Null when nobody has ever asked.</param>
internal sealed record ServiceabilityResponse(
    string Pincode,
    bool Deliverable,
    bool Covered,
    bool IsServiceable,
    bool PrepaidOk,
    bool CodOk,
    int? EtaDays,
    string? Courier,
    string? City,
    string? State,
    string? Reason,
    string? Message,
    DateTimeOffset? CheckedAt);

/// <summary>The delivery area, as an operator edits it (ADR-018).</summary>
/// <param name="Enabled">Whether deliveries are restricted at all.</param>
/// <param name="AllowedCities">Cities delivered to.</param>
/// <param name="AllowedPincodePrefixes">PIN-code prefixes delivered to.</param>
/// <param name="AllowedPincodes">Individual PIN codes delivered to.</param>
/// <param name="BlockedPincodes">PIN codes never delivered to, whatever else allows them.</param>
/// <param name="Message">What an out-of-area shopper is told.</param>
internal sealed record DeliveryCoverageResponse(
    bool Enabled,
    IReadOnlyList<string> AllowedCities,
    IReadOnlyList<string> AllowedPincodePrefixes,
    IReadOnlyList<string> AllowedPincodes,
    IReadOnlyList<string> BlockedPincodes,
    string Message);

/// <summary>Units of an order line inside a parcel.</summary>
/// <param name="OrderLineId">The order line.</param>
/// <param name="Sku">The stock-keeping unit.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Quantity">How many units.</param>
/// <param name="UnitWeightGrams">What one weighs.</param>
internal sealed record ShipmentLineResponse(
    Guid OrderLineId,
    string Sku,
    string Name,
    int Quantity,
    int UnitWeightGrams);

/// <summary>A parcel, as a list shows it.</summary>
/// <param name="Id">The consignment.</param>
/// <param name="OrderNumber">The order it belongs to.</param>
/// <param name="SubOrderNumber">The seller's part.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="Status">Where the parcel is.</param>
/// <param name="Courier">Who is carrying it.</param>
/// <param name="Awb">The air waybill.</param>
/// <param name="DestinationPincode">Where it is going.</param>
/// <param name="WeightGrams">What the packer weighed.</param>
/// <param name="CodAmount">Cash to collect at the door, or null for a prepaid parcel.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="ExpectedDeliveryAt">The courier's promise.</param>
/// <param name="CreatedAt">When the parcel was opened.</param>
internal sealed record ShipmentSummaryResponse(
    Guid Id,
    string OrderNumber,
    string SubOrderNumber,
    Guid? VendorId,
    string Status,
    string? Courier,
    string? Awb,
    string DestinationPincode,
    int WeightGrams,
    decimal? CodAmount,
    string CurrencyCode,
    DateTimeOffset? ExpectedDeliveryAt,
    DateTimeOffset CreatedAt);

/// <summary>One scan on a parcel.</summary>
/// <param name="Status">What this platform decided it means.</param>
/// <param name="CourierStatus">The courier's own word.</param>
/// <param name="Location">Where it happened.</param>
/// <param name="Remark">What the courier wrote.</param>
/// <param name="IsApplied">Whether it moved the parcel, or was only recorded.</param>
/// <param name="OccurredAt">When the courier says it happened.</param>
internal sealed record TrackingEventResponse(
    string Status,
    string? CourierStatus,
    string? Location,
    string? Remark,
    bool IsApplied,
    DateTimeOffset OccurredAt);

/// <summary>A short-lived link to a shipment's label.</summary>
/// <remarks>
/// A link rather than the bytes: a label carries a customer's name, address and telephone number,
/// so it lives in the private bucket and is reached through a URL that expires
/// (docs/07-security-compliance.md §5). The link is in the body rather than a redirect because the
/// back office opens it with a plain navigation, which carries no bearer token.
/// </remarks>
/// <param name="ShipmentId">The parcel this is the label for.</param>
/// <param name="Url">The signed link. Minting it is the grant; it carries no authorisation of its own.</param>
/// <param name="ExpiresAt">When the link stops working.</param>
/// <param name="FileName">What to call the file, so a save produces something recognisable.</param>
internal sealed record ShipmentLabelResponse(
    Guid ShipmentId,
    string Url,
    DateTimeOffset ExpiresAt,
    string FileName);

/// <summary>A parcel in full, with what is in it and where it has been.</summary>
/// <param name="Id">The consignment.</param>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="SubOrderNumber">Its number.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="Status">Where the parcel is.</param>
/// <param name="StatusReason">Why, in the courier's words.</param>
/// <param name="Provider">The adapter that booked it.</param>
/// <param name="Courier">Who is carrying it.</param>
/// <param name="ServiceName">Their name for the service.</param>
/// <param name="Awb">The air waybill.</param>
/// <param name="TrackingUrl">Where a shopper can watch it.</param>
/// <param name="LabelFileId">The stored label, once one exists.</param>
/// <param name="ManifestId">The handover sheet it went out on.</param>
/// <param name="WeightGrams">What the packer weighed.</param>
/// <param name="ChargedWeightGrams">What the courier billed for.</param>
/// <param name="LengthCm">Longest side.</param>
/// <param name="WidthCm">Width.</param>
/// <param name="HeightCm">Height.</param>
/// <param name="PickupPincode">Where it is collected from.</param>
/// <param name="DestinationPincode">Where it is going.</param>
/// <param name="CodAmount">Cash to collect at the door.</param>
/// <param name="DeclaredValue">What the goods are worth.</param>
/// <param name="FreightCharged">What the shopper paid for delivery.</param>
/// <param name="FreightCost">What the courier charged.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="IsReturn">Whether it is a reverse pickup.</param>
/// <param name="PickupScheduledAt">When a collection was asked for.</param>
/// <param name="PickedUpAt">When the courier took it.</param>
/// <param name="ExpectedDeliveryAt">Their promise.</param>
/// <param name="DeliveredAt">When it arrived.</param>
/// <param name="LastTrackedAt">When this platform last heard about it.</param>
/// <param name="DeliveryAttempts">How many attempts the courier has reported.</param>
/// <param name="Lines">What is in it.</param>
/// <param name="Tracking">Where it has been, newest first.</param>
internal sealed record ShipmentResponse(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    string SubOrderNumber,
    Guid? VendorId,
    string Status,
    string? StatusReason,
    string Provider,
    string? Courier,
    string? ServiceName,
    string? Awb,
    string? TrackingUrl,
    Guid? LabelFileId,
    Guid? ManifestId,
    int WeightGrams,
    int? ChargedWeightGrams,
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm,
    string? PickupPincode,
    string DestinationPincode,
    decimal? CodAmount,
    decimal DeclaredValue,
    decimal FreightCharged,
    decimal? FreightCost,
    string CurrencyCode,
    bool IsReturn,
    DateTimeOffset? PickupScheduledAt,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? ExpectedDeliveryAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? LastTrackedAt,
    int DeliveryAttempts,
    IReadOnlyList<ShipmentLineResponse> Lines,
    IReadOnlyList<TrackingEventResponse> Tracking);

/// <summary>One line of a pick list.</summary>
/// <param name="ShipmentId">The parcel to pack.</param>
/// <param name="OrderNumber">The order.</param>
/// <param name="SubOrderNumber">The seller's part.</param>
/// <param name="Sku">The stock-keeping unit to fetch.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Quantity">How many.</param>
/// <param name="DestinationPincode">Where it is going, so a packer can batch by region.</param>
/// <param name="DispatchDueAt">When the seller must have handed it over.</param>
/// <param name="WarehouseId">
/// The stock location the units were allocated from, or null when the offer was not stocked. With
/// two warehouses and no location on the row, both pickers are handed every parcel.
/// </param>
/// <param name="WarehouseName">What that location is called, so the row names a place not an id.</param>
internal sealed record PickListLineResponse(
    Guid ShipmentId,
    string OrderNumber,
    string SubOrderNumber,
    string Sku,
    string Name,
    int Quantity,
    string DestinationPincode,
    DateTimeOffset? DispatchDueAt,
    Guid? WarehouseId,
    string? WarehouseName);

/// <summary>A failed delivery attempt waiting for a decision.</summary>
/// <param name="Id">The report.</param>
/// <param name="ShipmentId">The parcel.</param>
/// <param name="OrderNumber">The order, which is what an operator quotes to the shopper.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CustomerId">The shopper, who is who somebody has to ring.</param>
/// <param name="Awb">The air waybill.</param>
/// <param name="AttemptNumber">Which attempt this was.</param>
/// <param name="ReasonCode">What this platform made of the courier's reason.</param>
/// <param name="Reason">The courier's reason, verbatim.</param>
/// <param name="Action">What was decided. <c>Pending</c> is the queue.</param>
/// <param name="ActionRemark">What the operator wrote.</param>
/// <param name="RescheduledFor">The date the shopper asked for.</param>
/// <param name="RaisedAt">When the attempt failed.</param>
/// <param name="ResolvedAt">When the report was closed.</param>
internal sealed record NdrResponse(
    Guid Id,
    Guid ShipmentId,
    string OrderNumber,
    Guid SubOrderId,
    Guid? VendorId,
    Guid CustomerId,
    string? Awb,
    int AttemptNumber,
    string ReasonCode,
    string? Reason,
    NdrAction Action,
    string? ActionRemark,
    DateTimeOffset? RescheduledFor,
    DateTimeOffset RaisedAt,
    DateTimeOffset? ResolvedAt);

/// <summary>A handover sheet.</summary>
/// <param name="Id">The sheet.</param>
/// <param name="Reference">The number printed on it.</param>
/// <param name="Courier">The courier collecting.</param>
/// <param name="VendorId">The seller handing over.</param>
/// <param name="PickupLocationId">The address being collected from.</param>
/// <param name="ShipmentCount">How many parcels.</param>
/// <param name="TotalWeightGrams">What they weigh together.</param>
/// <param name="FileId">The stored PDF.</param>
/// <param name="GeneratedAt">When it was produced.</param>
internal sealed record ManifestResponse(
    Guid Id,
    string Reference,
    string Courier,
    Guid? VendorId,
    Guid? PickupLocationId,
    int ShipmentCount,
    int TotalWeightGrams,
    Guid? FileId,
    DateTimeOffset GeneratedAt);

/// <summary>A stored courier webhook.</summary>
/// <param name="Id">The event.</param>
/// <param name="Provider">Which aggregator sent it.</param>
/// <param name="ProviderEventId">Its own id for it.</param>
/// <param name="EventType">What happened, in the courier's vocabulary.</param>
/// <param name="Awb">The parcel it named.</param>
/// <param name="SignatureValid">Whether the signature verified.</param>
/// <param name="Status">Where processing stands.</param>
/// <param name="Attempts">How many times it has been tried.</param>
/// <param name="ProcessError">Why the last attempt failed.</param>
/// <param name="ShipmentId">The parcel it turned out to concern.</param>
/// <param name="OccurredAt">When the courier says it happened.</param>
/// <param name="ReceivedAt">When this platform received it.</param>
/// <param name="ProcessedAt">When it was applied.</param>
internal sealed record CourierEventResponse(
    Guid Id,
    string Provider,
    string ProviderEventId,
    string EventType,
    string? Awb,
    bool SignatureValid,
    string Status,
    int Attempts,
    string? ProcessError,
    Guid? ShipmentId,
    DateTimeOffset? OccurredAt,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt);

/// <summary>
/// Turns this module's rows into the shapes its endpoints return.
/// </summary>
/// <remarks>
/// One place, so two endpoints cannot answer the same question with two differently-shaped
/// documents. The raw webhook payload is deliberately absent from every projection here: it is
/// evidence for the plumbing surface, which reads it through its own endpoint, and not something a
/// list of events should carry.
/// </remarks>
internal static class ShippingProjection
{
    /// <summary>Projects a zone.</summary>
    /// <param name="zone">The zone.</param>
    public static ShippingZoneResponse ToZone(ShippingZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return new ShippingZoneResponse(
            zone.Id,
            zone.Code,
            zone.Name,
            zone.States,
            [.. zone.PincodeRanges.Select(range => new PincodeRangeModel(range.From, range.To))],
            zone.Priority,
            zone.IsActive,
            zone.IsCatchAll);
    }

    /// <summary>Projects a rate rule.</summary>
    /// <param name="rate">The rule.</param>
    public static ShippingRateResponse ToRate(ShippingRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);

        return new ShippingRateResponse(
            rate.Id,
            rate.ZoneId,
            rate.VendorId,
            rate.Method.ToString(),
            rate.MinWeightGrams,
            rate.MaxWeightGrams,
            rate.MinOrderValue,
            rate.MaxOrderValue,
            rate.BaseRate,
            rate.PerKgRate,
            rate.FreeAbove,
            rate.CodFee,
            rate.IsCodAllowed,
            rate.CurrencyCode,
            rate.EtaMinDays,
            rate.EtaMaxDays,
            rate.IsActive);
    }

    /// <summary>Projects a parcel for a list.</summary>
    /// <param name="shipment">The consignment.</param>
    public static ShipmentSummaryResponse ToSummary(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        return new ShipmentSummaryResponse(
            shipment.Id,
            shipment.OrderNumber,
            shipment.SubOrderNumber,
            shipment.VendorId,
            shipment.Status.ToString(),
            shipment.Courier,
            shipment.Awb,
            shipment.DestinationPincode,
            shipment.WeightGrams,
            shipment.CodAmount,
            shipment.CurrencyCode,
            shipment.ExpectedDeliveryAt,
            shipment.CreatedAt);
    }

    /// <summary>Projects a parcel in full.</summary>
    /// <param name="shipment">The consignment.</param>
    /// <param name="tracking">Its scans, newest first.</param>
    public static ShipmentResponse ToShipment(Shipment shipment, IReadOnlyList<TrackingEvent> tracking)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(tracking);

        return new ShipmentResponse(
            shipment.Id,
            shipment.OrderId,
            shipment.OrderNumber,
            shipment.SubOrderId,
            shipment.SubOrderNumber,
            shipment.VendorId,
            shipment.Status.ToString(),
            shipment.StatusReason,
            shipment.Provider,
            shipment.Courier,
            shipment.ServiceName,
            shipment.Awb,
            shipment.TrackingUrl,
            shipment.LabelFileId,
            shipment.ManifestId,
            shipment.WeightGrams,
            shipment.ChargedWeightGrams,
            shipment.Dimensions.LengthCm,
            shipment.Dimensions.WidthCm,
            shipment.Dimensions.HeightCm,
            shipment.PickupPincode,
            shipment.DestinationPincode,
            shipment.CodAmount,
            shipment.DeclaredValue,
            shipment.FreightCharged,
            shipment.FreightCost,
            shipment.CurrencyCode,
            shipment.IsReturn,
            shipment.PickupScheduledAt,
            shipment.PickedUpAt,
            shipment.ExpectedDeliveryAt,
            shipment.DeliveredAt,
            shipment.LastTrackedAt,
            shipment.DeliveryAttempts,
            [
                .. shipment.Lines.Select(line => new ShipmentLineResponse(
                    line.OrderLineId,
                    line.Sku,
                    line.Name,
                    line.Quantity,
                    line.UnitWeightGrams)),
            ],
            [
                .. tracking.Select(entry => new TrackingEventResponse(
                    entry.Status.ToString(),
                    entry.CourierStatus,
                    entry.Location,
                    entry.Remark,
                    entry.IsApplied,
                    entry.OccurredAt)),
            ]);
    }

    /// <summary>Projects a failed-delivery report.</summary>
    /// <param name="record">The report.</param>
    public static NdrResponse ToNdr(NdrRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new NdrResponse(
            record.Id,
            record.ShipmentId,
            record.OrderNumber,
            record.SubOrderId,
            record.VendorId,
            record.CustomerId,
            record.Awb,
            record.AttemptNumber,
            record.ReasonCode.ToString(),
            record.Reason,
            record.Action,
            record.ActionRemark,
            record.RescheduledFor,
            record.RaisedAt,
            record.ResolvedAt);
    }

    /// <summary>Projects a handover sheet.</summary>
    /// <param name="manifest">The sheet.</param>
    public static ManifestResponse ToManifest(ShippingManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return new ManifestResponse(
            manifest.Id,
            manifest.Reference,
            manifest.Courier,
            manifest.VendorId,
            manifest.PickupLocationId,
            manifest.ShipmentCount,
            manifest.TotalWeightGrams,
            manifest.FileId,
            manifest.GeneratedAt);
    }

    /// <summary>Projects a stored webhook, without its payload.</summary>
    /// <param name="entry">The event.</param>
    public static CourierEventResponse ToCourierEvent(CourierEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new CourierEventResponse(
            entry.Id,
            entry.Provider,
            entry.ProviderEventId,
            entry.EventType,
            entry.Awb,
            entry.SignatureValid,
            entry.Status.ToString(),
            entry.Attempts,
            entry.ProcessError,
            entry.ShipmentId,
            entry.OccurredAt,
            entry.ReceivedAt,
            entry.ProcessedAt);
    }

    /// <summary>Projects a cached serviceability answer together with the store's delivery area.</summary>
    /// <param name="answer">What the cache says a courier will do.</param>
    /// <param name="covered">Whether this store delivers there.</param>
    /// <param name="city">The city, from reference data where there is any.</param>
    /// <param name="state">The state, likewise.</param>
    /// <param name="message">The operator's words for an out-of-area destination.</param>
    public static ServiceabilityResponse ToServiceability(
        Infrastructure.Serviceability.ServiceabilityAnswer answer,
        bool covered = true,
        string? city = null,
        string? state = null,
        string? message = null)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return new ServiceabilityResponse(
            answer.Pincode,
            covered && answer.IsServiceable,
            covered,
            answer.IsServiceable,
            answer.PrepaidOk,
            answer.CodOk,
            answer.EtaDays,
            answer.Courier,
            city ?? answer.City,
            state ?? answer.State,
            Reason(covered, answer.IsServiceable),
            covered ? null : message,
            answer.CheckedAt);
    }

    /// <summary>Which of the two checks refused a destination, in the code the API publishes.</summary>
    /// <remarks>
    /// Coverage first when both fail: it is the one an operator can reverse, and telling a shopper
    /// no courier goes to an address the store had already decided not to serve would be true and
    /// misleading.
    /// </remarks>
    private static string? Reason(bool covered, bool serviceable)
        => !covered ? "DELIVERY_AREA_NOT_COVERED"
            : !serviceable ? "PINCODE_NOT_SERVICEABLE"
            : null;
}
