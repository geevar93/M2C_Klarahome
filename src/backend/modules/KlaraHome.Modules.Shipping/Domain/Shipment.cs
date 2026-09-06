using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Shipping.Domain;

/// <summary>
/// A parcel's outside dimensions, in centimetres. Stored as <c>jsonb</c>.
/// </summary>
/// <remarks>
/// Together with a divisor this is the volumetric weight, which is what a courier charges on when a
/// parcel is bulky and light. Keeping the three measurements rather than the derived figure is what
/// lets a weight dispute be re-argued: the courier disagrees about the divisor at least as often as
/// about the box.
/// </remarks>
internal sealed class ShipmentDimensions
{
    /// <summary>Longest side, in centimetres.</summary>
    public decimal LengthCm { get; set; }

    /// <summary>Width, in centimetres.</summary>
    public decimal WidthCm { get; set; }

    /// <summary>Height, in centimetres.</summary>
    public decimal HeightCm { get; set; }

    /// <summary>Whether all three measurements are present.</summary>
    public bool IsMeasured => LengthCm > 0 && WidthCm > 0 && HeightCm > 0;

    /// <summary>
    /// The volumetric weight in grams, at the courier's divisor.
    /// </summary>
    /// <remarks>
    /// The Indian convention is <c>L × W × H ÷ 5000</c> in kilograms, and the divisor is
    /// configuration rather than a constant because it is the first thing an aggregator negotiates.
    /// </remarks>
    /// <param name="divisor">The courier's divisor. 5000 unless the contract says otherwise.</param>
    public int VolumetricGrams(int divisor)
        => !IsMeasured || divisor <= 0
            ? 0
            : (int)Math.Ceiling(LengthCm * WidthCm * HeightCm / divisor * 1000m);
}

/// <summary>
/// One order line, or part of one, inside a parcel (docs/03-database-design.md §4.10).
/// </summary>
/// <remarks>
/// A quantity rather than a reference, which is the whole of what makes a partial shipment
/// expressible: three of five units in today's parcel and two in next week's are two rows against
/// the same order line, and the sum of them is what has actually been sent.
/// </remarks>
internal sealed class ShipmentLine : Entity<Guid>, ITenantScoped
{
    private ShipmentLine(Guid id, Guid shipmentId, Guid orderLineId, string sku, int quantity)
        : base(id)
    {
        ShipmentId = shipmentId;
        OrderLineId = Guard.NotEmpty(orderLineId);
        Sku = Guard.NotNullOrWhiteSpace(sku);
        Quantity = Guard.Positive(quantity);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ShipmentLine()
    {
        Sku = string.Empty;
        Name = string.Empty;
    }

    /// <summary>The parcel.</summary>
    public Guid ShipmentId { get; private set; }

    /// <summary>The order line these units come from.</summary>
    public Guid OrderLineId { get; private set; }

    /// <summary>The stock-keeping unit, frozen at placement. What the packer reads.</summary>
    public string Sku { get; private set; }

    /// <summary>What it is called, for the pick list and the label.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>How many units of that line are in this parcel.</summary>
    public int Quantity { get; private set; }

    /// <summary>What one unit weighs, frozen from the order line.</summary>
    public int UnitWeightGrams { get; private set; }

    /// <summary>What these units are worth, for the courier's paperwork.</summary>
    public decimal DeclaredValue { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>What the units in this row weigh together.</summary>
    public int WeightGrams => UnitWeightGrams * Quantity;

    /// <summary>Puts units of an order line into a parcel.</summary>
    /// <param name="shipmentId">The parcel.</param>
    /// <param name="orderLineId">The order line.</param>
    /// <param name="sku">The stock-keeping unit.</param>
    /// <param name="name">What it is called.</param>
    /// <param name="quantity">How many units.</param>
    /// <param name="unitWeightGrams">What one unit weighs.</param>
    /// <param name="declaredValue">What the units are worth.</param>
    public static ShipmentLine Pack(
        Guid shipmentId,
        Guid orderLineId,
        string sku,
        string name,
        int quantity,
        int unitWeightGrams,
        decimal declaredValue)
        => new(UuidV7.New(), shipmentId, orderLineId, sku, quantity)
        {
            Name = name,
            UnitWeightGrams = Math.Max(0, unitWeightGrams),
            DeclaredValue = Guard.NotNegative(declaredValue),
        };
}

/// <summary>
/// A physical consignment with one air waybill (docs/02-domain-model.md §4).
/// </summary>
/// <remarks>
/// <para>
/// One sub-order may produce several: a partial shipment is two parcels against one seller's part,
/// and each carries its own AWB, its own weight and its own tracking. That is why the aggregate is
/// the parcel rather than the sub-order — a courier tracks boxes, not orders.
/// </para>
/// <para>
/// Two weights are kept and they are not the same number. <see cref="WeightGrams"/> is what the
/// packer put on the scale; <see cref="ChargedWeightGrams"/> is what the courier billed for, which
/// arrives days later and is routinely higher. Recording both is what makes a weight dispute a query
/// (docs/08-integrations.md §2), and recording only one is how a platform loses the argument.
/// </para>
/// <para>
/// Two freight figures are kept for the same reason. <see cref="FreightCharged"/> is what the
/// shopper paid, from this platform's own rate card; <see cref="FreightCost"/> is what the
/// aggregator invoiced. The margin on delivery is the difference, and it belongs on the row where
/// both facts already are.
/// </para>
/// <para>
/// The status only ever moves through <see cref="ShipmentLifecycle"/>, and the instants are stamped
/// by the transition rather than set by a caller. A courier that reports delivery twice therefore
/// cannot move the delivery time, and a caller cannot record a pickup that never had a transition.
/// </para>
/// </remarks>
internal sealed class Shipment : AggregateRoot<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private readonly List<ShipmentLine> _lines = [];

    private Shipment(
        Guid id,
        Guid orderId,
        string orderNumber,
        Guid subOrderId,
        string subOrderNumber,
        Guid vendorId,
        Guid customerId)
        : base(id)
    {
        OrderId = Guard.NotEmpty(orderId);
        OrderNumber = Guard.NotNullOrWhiteSpace(orderNumber);
        SubOrderId = Guard.NotEmpty(subOrderId);
        SubOrderNumber = Guard.NotNullOrWhiteSpace(subOrderNumber);
        VendorId = vendorId;
        CustomerId = customerId;
        Status = ShipmentStatus.Draft;
        CurrencyCode = Money.Inr;
        Dimensions = new ShipmentDimensions();
        DestinationPincode = string.Empty;
        Provider = string.Empty;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Shipment()
    {
        OrderNumber = string.Empty;
        SubOrderNumber = string.Empty;
        CurrencyCode = Money.Inr;
        Dimensions = new ShipmentDimensions();
        DestinationPincode = string.Empty;
        Provider = string.Empty;
    }

    /// <summary>The order.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Its number. What goes on the label and what a support call quotes.</summary>
    public string OrderNumber { get; private set; }

    /// <summary>The seller's part being dispatched.</summary>
    public Guid SubOrderId { get; private set; }

    /// <summary>Its number, which is what the seller's worklist shows.</summary>
    public string SubOrderNumber { get; private set; }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>The shopper.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Where the consignment is.</summary>
    public ShipmentStatus Status { get; private set; }

    /// <summary>Why it is there, when the reason is worth keeping. The courier's words, normally.</summary>
    public string? StatusReason { get; private set; }

    /// <summary>The adapter that booked it — the aggregator, or the manual fallback.</summary>
    public string Provider { get; private set; }

    /// <summary>The courier actually carrying it, once one has been chosen.</summary>
    public string? Courier { get; private set; }

    /// <summary>The courier's service name, kept verbatim for a support call.</summary>
    public string? ServiceName { get; private set; }

    /// <summary>The air waybill. The number that tracks it everywhere.</summary>
    public string? Awb { get; private set; }

    /// <summary>The aggregator's own id for the consignment, for every later call about it.</summary>
    public string? ProviderShipmentId { get; private set; }

    /// <summary>Where a shopper can watch it, when the courier publishes a page.</summary>
    public string? TrackingUrl { get; private set; }

    /// <summary>
    /// This platform's own rendered label, once one has been produced.
    /// </summary>
    /// <remarks>
    /// A media file, because it is a document this platform authored and registered. It is the
    /// fallback: a courier's own label is preferred wherever one exists, because ours carries no
    /// barcode.
    /// </remarks>
    public Guid? LabelFileId { get; private set; }

    /// <summary>
    /// The stored object key of the courier's own label PDF, once one has been fetched.
    /// </summary>
    /// <remarks>
    /// A key rather than a media file id, and that is the honest shape for it: it is an operational
    /// artefact with no gallery, no derivatives and no owner but this row, and registering it in the
    /// media library would put a customer's address into a browsable catalogue. It lives in the
    /// private bucket and is served through a signed link.
    /// </remarks>
    public string? LabelObjectKey { get; private set; }

    /// <summary>The manifest this parcel was handed over on, once it has been.</summary>
    public Guid? ManifestId { get; private set; }

    /// <summary>What the packer put on the scale, in grams.</summary>
    public int WeightGrams { get; private set; }

    /// <summary>What the courier billed for, in grams. Null until they say.</summary>
    public int? ChargedWeightGrams { get; private set; }

    /// <summary>The box, in centimetres.</summary>
    public ShipmentDimensions Dimensions { get; private set; }

    /// <summary>The seller's address it is collected from.</summary>
    public Guid? PickupLocationId { get; private set; }

    /// <summary>That address's PIN code. The first leg is priced from it.</summary>
    public string? PickupPincode { get; private set; }

    /// <summary>The destination's six-digit PIN code.</summary>
    public string DestinationPincode { get; private set; }

    /// <summary>The destination's <c>platform.states</c> row.</summary>
    public Guid? DestinationStateId { get; private set; }

    /// <summary>Cash to collect at the door, or null for a prepaid parcel.</summary>
    public decimal? CodAmount { get; private set; }

    /// <summary>What the goods are worth, for the courier's insurance and paperwork.</summary>
    public decimal DeclaredValue { get; private set; }

    /// <summary>What the shopper paid for delivery, from this platform's rate card.</summary>
    public decimal FreightCharged { get; private set; }

    /// <summary>What the aggregator invoiced for carrying it. Null until they say.</summary>
    public decimal? FreightCost { get; private set; }

    /// <summary>ISO 4217 code every amount here is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>Whether this is a reverse pickup — goods coming back rather than going out.</summary>
    public bool IsReturn { get; private set; }

    /// <summary>When a collection was asked for.</summary>
    public DateTimeOffset? PickupScheduledAt { get; private set; }

    /// <summary>When the courier took it. This is dispatch.</summary>
    public DateTimeOffset? PickedUpAt { get; private set; }

    /// <summary>When it reached the shopper.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>The courier's promise, when they make one.</summary>
    public DateTimeOffset? ExpectedDeliveryAt { get; private set; }

    /// <summary>When it was called off.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>
    /// When this platform last heard anything about it.
    /// </summary>
    /// <remarks>
    /// What the polling fallback works from: a consignment with no update in a day is one whose
    /// webhooks are not arriving, and asking the courier directly is cheaper than discovering it from
    /// a customer (docs/08-integrations.md §2).
    /// </remarks>
    public DateTimeOffset? LastTrackedAt { get; private set; }

    /// <summary>How many delivery attempts the courier has reported.</summary>
    public int DeliveryAttempts { get; private set; }

    /// <summary>What is in it.</summary>
    public IReadOnlyList<ShipmentLine> Lines => _lines;

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>What the lines weigh together, before any volumetric adjustment.</summary>
    public int DeadWeightGrams => _lines.Sum(line => line.WeightGrams);

    /// <summary>Whether a courier has been booked. Everything before this is packing.</summary>
    public bool IsBooked => !string.IsNullOrWhiteSpace(Awb);

    /// <summary>Whether cash is to be collected at the door.</summary>
    public bool IsCod => CodAmount is > 0m;

    /// <summary>Opens a parcel for a confirmed seller's part. Nothing has been booked yet.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="orderNumber">Its number.</param>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="subOrderNumber">Its number.</param>
    /// <param name="vendorId">The seller.</param>
    /// <param name="customerId">The shopper.</param>
    public static Shipment Draft(
        Guid orderId,
        string orderNumber,
        Guid subOrderId,
        string subOrderNumber,
        Guid vendorId,
        Guid customerId)
        => new(UuidV7.New(), orderId, orderNumber, subOrderId, subOrderNumber, vendorId, customerId);

    /// <summary>Records where the parcel is going and what is riding on it.</summary>
    /// <param name="pincode">The destination's six-digit PIN code.</param>
    /// <param name="stateId">The destination's state.</param>
    /// <param name="codAmount">Cash to collect at the door, or null for a prepaid parcel.</param>
    /// <param name="declaredValue">What the goods are worth.</param>
    /// <param name="freightCharged">What the shopper paid for delivery.</param>
    /// <param name="currencyCode">ISO 4217 code the amounts are in.</param>
    public void Address(
        string pincode,
        Guid? stateId,
        decimal? codAmount,
        decimal declaredValue,
        decimal freightCharged,
        string currencyCode)
    {
        DestinationPincode = Guard.NotNullOrWhiteSpace(pincode);
        DestinationStateId = stateId;
        CodAmount = codAmount is { } cash ? Guard.NotNegative(cash) : null;
        DeclaredValue = Guard.NotNegative(declaredValue);
        FreightCharged = Guard.NotNegative(freightCharged);
        CurrencyCode = Guard.NotNullOrWhiteSpace(currencyCode);
    }

    /// <summary>Records the seller's address it will be collected from.</summary>
    /// <param name="pickupLocationId">The address.</param>
    /// <param name="pincode">Its PIN code.</param>
    public void CollectFrom(Guid? pickupLocationId, string? pincode)
    {
        PickupLocationId = pickupLocationId;
        PickupPincode = pincode;
    }

    /// <summary>Marks the parcel as goods coming back rather than going out.</summary>
    public void MarkReturn() => IsReturn = true;

    /// <summary>Puts units into the parcel.</summary>
    /// <param name="line">The units.</param>
    public void Pack(ShipmentLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _lines.Add(line);
    }

    /// <summary>Empties the parcel, so a packer can redo it before it is booked.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Unpack()
    {
        if (IsBooked)
        {
            return false;
        }

        _lines.Clear();
        return true;
    }

    /// <summary>
    /// Records what the packer weighed and measured.
    /// </summary>
    /// <remarks>
    /// Refused once the parcel is booked. The courier priced the consignment on the figures it was
    /// given, and changing them afterwards would leave the platform's record disagreeing with the
    /// waybill — which is the one document a weight dispute is argued from.
    /// </remarks>
    /// <param name="weightGrams">What the scale said.</param>
    /// <param name="lengthCm">Longest side.</param>
    /// <param name="widthCm">Width.</param>
    /// <param name="heightCm">Height.</param>
    /// <returns>Whether anything changed.</returns>
    public bool CaptureWeight(int weightGrams, decimal lengthCm, decimal widthCm, decimal heightCm)
    {
        if (IsBooked)
        {
            return false;
        }

        WeightGrams = Math.Max(0, weightGrams);
        Dimensions = new ShipmentDimensions
        {
            LengthCm = Math.Max(0m, lengthCm),
            WidthCm = Math.Max(0m, widthCm),
            HeightCm = Math.Max(0m, heightCm),
        };

        return true;
    }

    /// <summary>
    /// The weight a courier prices on: the greater of what it weighs and what it takes up.
    /// </summary>
    /// <remarks>
    /// Falls back to the lines' own weights when nobody has put the parcel on a scale, so a quote can
    /// be given before anything is packed. That fallback is an estimate and is deliberately never
    /// written to <see cref="WeightGrams"/> — an estimate recorded as a measurement is how a platform
    /// finds out about a weight dispute from an invoice.
    /// </remarks>
    /// <param name="volumetricDivisor">The courier's divisor. 5000 unless the contract says otherwise.</param>
    public int ChargeableWeightGrams(int volumetricDivisor)
    {
        var dead = WeightGrams > 0 ? WeightGrams : DeadWeightGrams;

        return Math.Max(dead, Dimensions.VolumetricGrams(volumetricDivisor));
    }

    /// <summary>
    /// Records that a courier has accepted the consignment.
    /// </summary>
    /// <remarks>
    /// The moment the parcel stops being a packing job and becomes a booking. It is idempotent by
    /// refusal rather than by overwrite: a second booking against a parcel that already has an AWB
    /// would leave two waybills in the world for one box, and only one of them tracked.
    /// </remarks>
    /// <param name="provider">The adapter that booked it.</param>
    /// <param name="courier">The courier carrying it.</param>
    /// <param name="serviceName">Their name for the service.</param>
    /// <param name="awb">The air waybill.</param>
    /// <param name="providerShipmentId">The aggregator's id for it.</param>
    /// <param name="trackingUrl">Where a shopper can watch it.</param>
    /// <param name="expectedDeliveryAt">The courier's promise.</param>
    /// <param name="freightCost">What the aggregator says it will charge.</param>
    /// <param name="at">When it was booked.</param>
    /// <returns>Whether anything changed.</returns>
    public bool Book(
        string provider,
        string courier,
        string? serviceName,
        string awb,
        string? providerShipmentId,
        string? trackingUrl,
        DateTimeOffset? expectedDeliveryAt,
        decimal? freightCost,
        DateTimeOffset at)
    {
        if (IsBooked || Status is not ShipmentStatus.Draft)
        {
            return false;
        }

        Provider = Guard.NotNullOrWhiteSpace(provider);
        Courier = Guard.NotNullOrWhiteSpace(courier);
        ServiceName = serviceName;
        Awb = Guard.NotNullOrWhiteSpace(awb);
        ProviderShipmentId = providerShipmentId;
        TrackingUrl = trackingUrl;
        ExpectedDeliveryAt = expectedDeliveryAt;
        FreightCost = freightCost;
        Status = ShipmentStatus.Created;
        LastTrackedAt = at;

        // The weight the courier was told, if nobody weighed it. Recorded now rather than left at
        // zero, because from here on it is what the waybill says and a dispute is argued from it.
        if (WeightGrams == 0)
        {
            WeightGrams = DeadWeightGrams;
        }

        return true;
    }

    /// <summary>Attaches this platform's own rendered label.</summary>
    /// <param name="fileId">The stored PDF.</param>
    /// <param name="at">When it was produced.</param>
    public void AttachLabel(Guid fileId, DateTimeOffset at)
    {
        LabelFileId = fileId;
        MarkLabelled(at);
    }

    /// <summary>Attaches the courier's own label, which is the one a packer should print.</summary>
    /// <param name="objectKey">Where the PDF is stored, in the private bucket.</param>
    /// <param name="at">When it was fetched.</param>
    public void AttachCourierLabel(string objectKey, DateTimeOffset at)
    {
        LabelObjectKey = Guard.NotNullOrWhiteSpace(objectKey);
        MarkLabelled(at);
    }

    /// <summary>Whether there is anything to print.</summary>
    public bool HasLabel => LabelFileId is not null || LabelObjectKey is { Length: > 0 };

    private void MarkLabelled(DateTimeOffset at)
    {
        if (Status == ShipmentStatus.Created)
        {
            Status = ShipmentStatus.LabelGenerated;
            LastTrackedAt = at;
        }
    }

    /// <summary>Attaches the manifest the parcel was handed over on.</summary>
    /// <param name="manifestId">The manifest.</param>
    public void AttachManifest(Guid manifestId) => ManifestId = manifestId;

    /// <summary>Records that a collection has been asked for.</summary>
    /// <param name="scheduledFor">When the courier is coming.</param>
    /// <param name="at">When the request was made.</param>
    /// <returns>Whether anything changed.</returns>
    public bool SchedulePickup(DateTimeOffset scheduledFor, DateTimeOffset at)
    {
        if (!Advance(ShipmentStatus.PickupScheduled, at, reason: null))
        {
            return false;
        }

        PickupScheduledAt = scheduledFor;
        return true;
    }

    /// <summary>Records what the courier actually billed for.</summary>
    /// <remarks>
    /// Arrives days after dispatch, in an invoice or a reconciliation file, and is deliberately kept
    /// beside the packer's own figure rather than replacing it. Both together are the weight dispute.
    /// </remarks>
    /// <param name="chargedWeightGrams">What the courier billed for.</param>
    /// <param name="freightCost">What they charged.</param>
    public void RecordCharges(int? chargedWeightGrams, decimal? freightCost)
    {
        if (chargedWeightGrams is { } charged and > 0)
        {
            ChargedWeightGrams = charged;
        }

        if (freightCost is { } cost and >= 0m)
        {
            FreightCost = cost;
        }
    }

    /// <summary>
    /// Moves the consignment, if the machine has that edge.
    /// </summary>
    /// <remarks>
    /// The single place a status changes, and the single place the instants are stamped. A courier
    /// reporting delivery three times therefore records one delivery time — the first — which is what
    /// a return window has to be measured from.
    /// </remarks>
    /// <param name="next">Where to move it.</param>
    /// <param name="at">When the courier says it happened.</param>
    /// <param name="reason">Why, in the courier's words.</param>
    /// <returns>Whether anything changed.</returns>
    public bool Advance(ShipmentStatus next, DateTimeOffset at, string? reason)
    {
        if (!ShipmentLifecycle.IsTransitionAllowed(Status, next))
        {
            return false;
        }

        Status = next;
        StatusReason = Clip(reason, 500);
        LastTrackedAt = at;

        switch (next)
        {
            case ShipmentStatus.PickedUp:
                PickedUpAt ??= at;
                break;

            case ShipmentStatus.Delivered:
                DeliveredAt ??= at;
                break;

            case ShipmentStatus.Cancelled:
                CancelledAt ??= at;
                break;

            case ShipmentStatus.Exception:
                DeliveryAttempts++;
                break;

            default:
                break;
        }

        return true;
    }

    /// <summary>Records that this platform has heard from the courier, whatever they said.</summary>
    /// <param name="at">When.</param>
    public void MarkTracked(DateTimeOffset at)
        => LastTrackedAt = LastTrackedAt is { } last && last > at ? last : at;

    /// <summary>Whether the polling fallback should ask the courier about this parcel.</summary>
    /// <remarks>
    /// Only a booked parcel that is not finished, and only one nobody has heard from. A draft has no
    /// courier to ask and a delivered one has nothing left to say.
    /// </remarks>
    /// <param name="now">The current instant.</param>
    /// <param name="silence">How long a silence is too long.</param>
    public bool IsStale(DateTimeOffset now, TimeSpan silence)
        => IsBooked
           && !ShipmentLifecycle.IsTerminal(Status)
           && (LastTrackedAt is not { } last || now - last >= silence);

    private static string? Clip(string? value, int max)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= max ? value : value[..max];
}
