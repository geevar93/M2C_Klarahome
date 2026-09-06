using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Returns.Domain;

/// <summary>
/// What was bought, frozen at the moment the return was asked for.
/// </summary>
/// <remarks>
/// A copy rather than a join, for the reason every other snapshot in this platform is one: the
/// catalogue is edited, and a return screen that re-read it would show the shopper a product that is
/// not the one in the box. The tax figures matter most — a credit note has to credit the tax that
/// was charged, and today's GST rate is not evidence of what was charged last month.
/// </remarks>
internal sealed class ReturnLineSnapshot
{
    /// <summary>What it is called, frozen at placement.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The picture, for the shopper's return screen.</summary>
    public Guid? ImageFileId { get; set; }

    /// <summary>The HSN the tax was charged under, which the credit note repeats.</summary>
    public string? HsnCode { get; set; }

    /// <summary>The variant, so a replacement can be dispatched against the same thing.</summary>
    public Guid VariantId { get; set; }

    /// <summary>What one unit cost, inclusive of tax.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>The rate charged, as a percentage.</summary>
    public decimal GstRate { get; set; }

    /// <summary>What one unit's tax was computed on.</summary>
    public decimal UnitTaxableValue { get; set; }

    /// <summary>Central GST on one unit.</summary>
    public decimal UnitCgst { get; set; }

    /// <summary>State GST on one unit.</summary>
    public decimal UnitSgst { get; set; }

    /// <summary>Integrated GST on one unit.</summary>
    public decimal UnitIgst { get; set; }

    /// <summary>Compensation cess on one unit.</summary>
    public decimal UnitCess { get; set; }
}

/// <summary>
/// Units of one order line coming back (docs/03-database-design.md §4.11).
/// </summary>
/// <remarks>
/// <para>
/// A quantity rather than a reference, which is what makes a partial return expressible: one of
/// three units going back is one unit of movement and one third of the line's tax, and a schema that
/// could only return whole lines would force a shopper to keep two things they did not want.
/// </para>
/// <para>
/// The refund figures are computed once, at request time, and then held. They are not recomputed at
/// QC, because the price the shopper paid cannot change between the two — and if the arithmetic ever
/// did change, a refund that silently moved would be worse than one that was wrong in a way somebody
/// could see.
/// </para>
/// </remarks>
internal sealed class ReturnLine : Entity<Guid>, ITenantScoped
{
    private ReturnLine(Guid id, Guid returnId, Guid orderLineId, Guid listingId, string sku, int quantity)
        : base(id)
    {
        ReturnId = returnId;
        OrderLineId = Guard.NotEmpty(orderLineId);
        ListingId = Guard.NotEmpty(listingId);
        Sku = Guard.NotNullOrWhiteSpace(sku);
        Quantity = Guard.Positive(quantity);
        Disposition = ReturnDisposition.Pending;
        Snapshot = new ReturnLineSnapshot();
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ReturnLine()
    {
        Sku = string.Empty;
        Snapshot = new ReturnLineSnapshot();
    }

    /// <summary>The RMA.</summary>
    public Guid ReturnId { get; private set; }

    /// <summary>The order line the units came from.</summary>
    public Guid OrderLineId { get; private set; }

    /// <summary>The offer sold. It is what stock goes back against.</summary>
    public Guid ListingId { get; private set; }

    /// <summary>The stock-keeping unit, frozen at placement.</summary>
    public string Sku { get; private set; }

    /// <summary>What was bought, frozen when the return was asked for.</summary>
    public ReturnLineSnapshot Snapshot { get; private set; }

    /// <summary>How many units the shopper asked to send back.</summary>
    public int Quantity { get; private set; }

    /// <summary>How many of them quality control accepted. Zero until it has looked.</summary>
    public int QuantityAccepted { get; private set; }

    /// <summary>What the goods are worth back, before tax, for the units asked for.</summary>
    public decimal TaxableValue { get; private set; }

    /// <summary>Central GST being credited on these units.</summary>
    public decimal Cgst { get; private set; }

    /// <summary>State GST being credited on these units.</summary>
    public decimal Sgst { get; private set; }

    /// <summary>Integrated GST being credited on these units.</summary>
    public decimal Igst { get; private set; }

    /// <summary>Compensation cess being credited on these units.</summary>
    public decimal Cess { get; private set; }

    /// <summary>What goes back to the shopper for these units, inclusive of tax.</summary>
    public decimal RefundAmount { get; private set; }

    /// <summary>What quality control decided to do with them.</summary>
    public ReturnDisposition Disposition { get; private set; }

    /// <summary>What the inspector wrote about this line specifically.</summary>
    public string? QcNote { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Every tax head together, for the units asked for.</summary>
    public decimal TaxAmount => Cgst + Sgst + Igst + Cess;

    /// <summary>
    /// What is actually owed on this line once QC has looked, which is not what was asked for.
    /// </summary>
    /// <remarks>
    /// Apportioned by accepted quantity rather than stored, because the two numbers diverge exactly
    /// once — at QC — and holding a second money column that has to be kept in step with the first
    /// is how they come to disagree.
    /// </remarks>
    public decimal AcceptedRefund
        => Quantity <= 0 || QuantityAccepted <= 0
            ? 0m
            : QuantityAccepted >= Quantity
                ? RefundAmount
                : Math.Round(RefundAmount * QuantityAccepted / Quantity, 4, MidpointRounding.AwayFromZero);

    /// <summary>What was accepted is worth this much before tax.</summary>
    public decimal AcceptedTaxableValue
        => Quantity <= 0 || QuantityAccepted <= 0
            ? 0m
            : QuantityAccepted >= Quantity
                ? TaxableValue
                : Math.Round(TaxableValue * QuantityAccepted / Quantity, 4, MidpointRounding.AwayFromZero);

    /// <summary>Puts units of an order line on a return.</summary>
    /// <param name="returnId">The RMA.</param>
    /// <param name="orderLineId">The order line.</param>
    /// <param name="listingId">The offer sold.</param>
    /// <param name="sku">The stock-keeping unit.</param>
    /// <param name="quantity">How many units.</param>
    public static ReturnLine For(Guid returnId, Guid orderLineId, Guid listingId, string sku, int quantity)
        => new(UuidV7.New(), returnId, orderLineId, listingId, sku, quantity);

    /// <summary>Freezes what was bought.</summary>
    /// <param name="snapshot">The product as it was sold.</param>
    public void Capture(ReturnLineSnapshot snapshot)
        => Snapshot = Guard.NotNull(snapshot);

    /// <summary>Records what these units are worth back, and how the tax splits.</summary>
    /// <param name="taxableValue">What the tax was computed on.</param>
    /// <param name="cgst">Central GST.</param>
    /// <param name="sgst">State GST.</param>
    /// <param name="igst">Integrated GST.</param>
    /// <param name="cess">Compensation cess.</param>
    /// <param name="refundAmount">What goes back, inclusive of tax.</param>
    public void Value(
        decimal taxableValue,
        decimal cgst,
        decimal sgst,
        decimal igst,
        decimal cess,
        decimal refundAmount)
    {
        TaxableValue = Guard.NotNegative(taxableValue);
        Cgst = Guard.NotNegative(cgst);
        Sgst = Guard.NotNegative(sgst);
        Igst = Guard.NotNegative(igst);
        Cess = Guard.NotNegative(cess);
        RefundAmount = Guard.NotNegative(refundAmount);
    }

    /// <summary>
    /// Records what quality control decided.
    /// </summary>
    /// <remarks>
    /// The accepted quantity is clamped to what was asked for rather than refused. An inspector
    /// counting four units where the shopper declared three has found a discrepancy worth a note,
    /// not a refund for a unit nobody sold.
    /// </remarks>
    /// <param name="accepted">How many units passed.</param>
    /// <param name="disposition">What becomes of them.</param>
    /// <param name="note">What the inspector wrote.</param>
    public void Inspect(int accepted, ReturnDisposition disposition, string? note)
    {
        QuantityAccepted = Math.Clamp(accepted, 0, Quantity);
        Disposition = disposition;
        QcNote = note;
    }
}

/// <summary>
/// A return merchandise authorisation (docs/02-domain-model.md §5.3).
/// </summary>
/// <remarks>
/// <para>
/// The aggregate is the request, not the parcel and not the refund. One RMA covers one seller's part
/// of one order, because that is the unit a shopper thinks in ("send this back") and the unit the
/// paperwork needs — a credit note is raised under one seller's GSTIN, and a return spanning two
/// sellers would need two of them.
/// </para>
/// <para>
/// It holds three amounts and they are all different questions. <see cref="EstimatedRefund"/> is
/// what the shopper was quoted when they asked. <see cref="ApprovedAmount"/> is what somebody agreed
/// to, which may be less. <see cref="RefundAmount"/> is what actually went back after quality
/// control and after the return freight was deducted — and it is the only one that is money.
/// Collapsing them would make it impossible to answer why a shopper got less than the screen said,
/// which is the second commonest returns support call there is.
/// </para>
/// <para>
/// Nothing here sends money or moves stock. The aggregate records decisions; the seams do the work,
/// and this separation is what lets a refund fail at the gateway without the return forgetting that
/// it was approved.
/// </para>
/// </remarks>
internal sealed class ReturnRequest : AggregateRoot<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private readonly List<ReturnLine> _lines = [];
    private readonly List<Guid> _evidenceFileIds = [];

    private ReturnRequest(
        Guid id,
        string returnNumber,
        Guid orderId,
        string orderNumber,
        Guid subOrderId,
        string subOrderNumber,
        Guid vendorId,
        Guid customerId,
        ReturnType type,
        string reasonCode,
        string currencyCode,
        DateTimeOffset requestedAt)
        : base(id)
    {
        ReturnNumber = returnNumber;
        OrderId = orderId;
        OrderNumber = orderNumber;
        SubOrderId = subOrderId;
        SubOrderNumber = subOrderNumber;
        VendorId = vendorId;
        CustomerId = customerId;
        Type = type;
        ReasonCode = reasonCode;
        CurrencyCode = currencyCode;
        RequestedAt = requestedAt;
        Status = ReturnStatus.Requested;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ReturnRequest()
    {
        ReturnNumber = string.Empty;
        OrderNumber = string.Empty;
        SubOrderNumber = string.Empty;
        ReasonCode = string.Empty;
        CurrencyCode = Money.Inr;
    }

    /// <summary>The number the shopper quotes, as <c>RMA-2609-000184</c>.</summary>
    public string ReturnNumber { get; private set; }

    /// <summary>The order.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Its number.</summary>
    public string OrderNumber { get; private set; }

    /// <summary>The seller's part the goods came from.</summary>
    public Guid SubOrderId { get; private set; }

    /// <summary>Its number, which is what the seller's worklist shows.</summary>
    public string SubOrderNumber { get; private set; }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>The shopper.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Money back, or the same thing again.</summary>
    public ReturnType Type { get; private set; }

    /// <summary>Where it stands.</summary>
    public ReturnStatus Status { get; private set; }

    /// <summary>Why, as a code from the configured list.</summary>
    public string ReasonCode { get; private set; }

    /// <summary>Why, in the shopper's own words.</summary>
    public string? ReasonNote { get; private set; }

    /// <summary>The photographs the shopper attached. Stored as <c>jsonb</c>.</summary>
    public IReadOnlyList<Guid> EvidenceFileIds => _evidenceFileIds;

    /// <summary>What the shopper was quoted when they asked, inclusive of tax.</summary>
    public decimal EstimatedRefund { get; private set; }

    /// <summary>What was agreed to, inclusive of tax. Zero until somebody approves.</summary>
    public decimal ApprovedAmount { get; private set; }

    /// <summary>What actually went back, inclusive of tax. Zero until it did.</summary>
    public decimal RefundAmount { get; private set; }

    /// <summary>What the shopper was charged for the reverse pickup, and had deducted.</summary>
    public decimal ReturnShippingFee { get; private set; }

    /// <summary>
    /// The delivery charge on the original parcel that goes back too, inclusive of its own tax.
    /// </summary>
    /// <remarks>
    /// An amount rather than a flag, and the amount is decided once — at approval, from the order's
    /// own freight figure — so a refund calculated later cannot arrive at a different one. Zero on
    /// every partial return: a shopper keeping two of three items has had the parcel delivered.
    /// </remarks>
    public decimal ShippingRefundAmount { get; private set; }

    /// <summary>Where the money went, or null while it has not.</summary>
    public ReturnRefundMode? RefundMode { get; private set; }

    /// <summary>ISO 4217 code every amount on this return is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>Whether a courier has to collect, or the shopper keeps the goods.</summary>
    public bool IsPickupRequired { get; private set; } = true;

    /// <summary>The reverse consignment, once one is booked.</summary>
    public Guid? PickupShipmentId { get; private set; }

    /// <summary>The waybill it is travelling back on.</summary>
    public string? PickupAwb { get; private set; }

    /// <summary>When a courier is due to call.</summary>
    public DateTimeOffset? PickupScheduledFor { get; private set; }

    /// <summary>The refund raised in the Payments module, once one is.</summary>
    public Guid? RefundId { get; private set; }

    /// <summary>The credit note raised for it, once one is.</summary>
    public Guid? CreditNoteId { get; private set; }

    /// <summary>The replacement order placed for it, when the shopper wanted one.</summary>
    public Guid? ReplacementOrderId { get; private set; }

    /// <summary>Whether quality control passed it. Null until it has looked.</summary>
    public bool? QcPassed { get; private set; }

    /// <summary>What the inspector wrote.</summary>
    public string? QcNotes { get; private set; }

    /// <summary>Who inspected it.</summary>
    public Guid? QcBy { get; private set; }

    /// <summary>Why it was refused.</summary>
    public string? RejectedReason { get; private set; }

    /// <summary>When the shopper asked.</summary>
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>When it was approved.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>Who approved it.</summary>
    public Guid? ApprovedBy { get; private set; }

    /// <summary>When the courier collected it.</summary>
    public DateTimeOffset? PickedAt { get; private set; }

    /// <summary>When the warehouse booked it in.</summary>
    public DateTimeOffset? ReceivedAt { get; private set; }

    /// <summary>When it was inspected.</summary>
    public DateTimeOffset? InspectedAt { get; private set; }

    /// <summary>When the money went back.</summary>
    public DateTimeOffset? RefundedAt { get; private set; }

    /// <summary>When it finished.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>The units on it.</summary>
    public IReadOnlyList<ReturnLine> Lines => _lines;

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

    /// <summary>How many units the shopper asked to send back, across every line.</summary>
    public int TotalQuantity => _lines.Sum(line => line.Quantity);

    /// <summary>How many units quality control accepted, across every line.</summary>
    public int TotalAccepted => _lines.Sum(line => line.QuantityAccepted);

    /// <summary>What quality control accepted is worth back, inclusive of tax, before any deduction.</summary>
    public decimal AcceptedValue => _lines.Sum(line => line.AcceptedRefund);

    /// <summary>Whether nothing further can happen to it.</summary>
    public bool IsTerminal => ReturnLifecycle.IsTerminal(Status);

    /// <summary>Whether the goods are still with the shopper.</summary>
    public bool IsBeforeCollection => ReturnLifecycle.IsBeforeCollection(Status);

    /// <summary>Opens an RMA against one seller's part of one order.</summary>
    /// <param name="returnNumber">The allocated number.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="orderNumber">Its number.</param>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="subOrderNumber">Its number.</param>
    /// <param name="vendorId">The seller.</param>
    /// <param name="customerId">The shopper.</param>
    /// <param name="type">Money back, or a replacement.</param>
    /// <param name="reasonCode">Why, as a configured code.</param>
    /// <param name="currencyCode">The currency every amount is in.</param>
    /// <param name="requestedAt">When they asked.</param>
    public static ReturnRequest Raise(
        string returnNumber,
        Guid orderId,
        string orderNumber,
        Guid subOrderId,
        string subOrderNumber,
        Guid vendorId,
        Guid customerId,
        ReturnType type,
        string reasonCode,
        string currencyCode,
        DateTimeOffset requestedAt)
        => new(
            UuidV7.New(),
            Guard.NotNullOrWhiteSpace(returnNumber),
            Guard.NotEmpty(orderId),
            Guard.NotNullOrWhiteSpace(orderNumber),
            Guard.NotEmpty(subOrderId),
            Guard.NotNullOrWhiteSpace(subOrderNumber),
            Guard.NotEmpty(vendorId),
            Guard.NotEmpty(customerId),
            type,
            Guard.NotNullOrWhiteSpace(reasonCode),
            currencyCode,
            requestedAt);

    /// <summary>Adds units to the request.</summary>
    /// <param name="line">The line.</param>
    public void Add(ReturnLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _lines.Add(line);
    }

    /// <summary>Records why, in the shopper's own words, and what they attached.</summary>
    /// <param name="note">The shopper's words.</param>
    /// <param name="evidenceFileIds">The photographs.</param>
    public void Explain(string? note, IReadOnlyCollection<Guid>? evidenceFileIds)
    {
        ReasonNote = note;

        _evidenceFileIds.Clear();

        if (evidenceFileIds is { Count: > 0 })
        {
            _evidenceFileIds.AddRange(evidenceFileIds.Distinct());
        }
    }

    /// <summary>Records what the shopper was quoted.</summary>
    /// <param name="amount">What it would come to, inclusive of tax.</param>
    public void Quote(decimal amount) => EstimatedRefund = Guard.NotNegative(amount);

    /// <summary>
    /// Moves the return, if the machine has the edge and this actor may take it.
    /// </summary>
    /// <remarks>
    /// The only way a status changes. Every timestamp that belongs to a state is written here rather
    /// than by the caller, so a return that reached <see cref="ReturnStatus.Received"/> without a
    /// <see cref="ReceivedAt"/> is not a state this aggregate can be in.
    /// </remarks>
    /// <param name="next">Where it is going.</param>
    /// <param name="actor">Who is asking.</param>
    /// <param name="actorId">The user, when there is one.</param>
    /// <param name="occurredAt">When.</param>
    public bool Transition(ReturnStatus next, ReturnActor actor, Guid? actorId, DateTimeOffset occurredAt)
    {
        if (!ReturnLifecycle.IsAllowed(Status, next, actor))
        {
            return false;
        }

        Status = next;

        switch (next)
        {
            case ReturnStatus.Approved:
                ApprovedAt ??= occurredAt;
                ApprovedBy ??= actorId;
                break;
            case ReturnStatus.Picked:
                PickedAt ??= occurredAt;
                break;
            case ReturnStatus.Received:
                ReceivedAt ??= occurredAt;
                break;
            case ReturnStatus.QcPassed:
            case ReturnStatus.QcFailed:
                InspectedAt ??= occurredAt;
                QcPassed = next == ReturnStatus.QcPassed;
                QcBy ??= actorId;
                break;
            case ReturnStatus.Refunded:
                RefundedAt ??= occurredAt;
                break;
            case ReturnStatus.Rejected:
            case ReturnStatus.Closed:
            case ReturnStatus.Cancelled:
                ClosedAt ??= occurredAt;
                break;
            default:
                break;
        }

        return true;
    }

    /// <summary>Records what was agreed to, and whether the goods have to come back.</summary>
    /// <param name="amount">What was approved, inclusive of tax.</param>
    /// <param name="pickupRequired">Whether a courier collects.</param>
    public void Agree(decimal amount, bool pickupRequired)
    {
        ApprovedAmount = Guard.NotNegative(amount);
        IsPickupRequired = pickupRequired;
    }

    /// <summary>Records why it was refused.</summary>
    /// <param name="reason">Why, in words the shopper is shown.</param>
    public void Refuse(string? reason) => RejectedReason = reason;

    /// <summary>Records the reverse consignment a courier agreed to.</summary>
    /// <param name="shipmentId">The consignment.</param>
    /// <param name="awb">The waybill.</param>
    /// <param name="scheduledFor">When the courier will call.</param>
    public void Collect(Guid? shipmentId, string? awb, DateTimeOffset? scheduledFor)
    {
        PickupShipmentId = shipmentId;
        PickupAwb = awb;
        PickupScheduledFor = scheduledFor;
    }

    /// <summary>Records what the inspector concluded overall.</summary>
    /// <param name="notes">What they wrote.</param>
    public void RecordQc(string? notes) => QcNotes = notes;

    /// <summary>Records what the shopper was charged for the reverse pickup.</summary>
    /// <param name="fee">The fee, deducted from what goes back.</param>
    public void ChargePickup(decimal fee) => ReturnShippingFee = Guard.NotNegative(fee);

    /// <summary>Records that the original delivery charge goes back as well.</summary>
    /// <param name="amount">The freight being credited, inclusive of its own tax.</param>
    public void RefundShipping(decimal amount) => ShippingRefundAmount = Guard.NotNegative(amount);

    /// <summary>Records where the money went.</summary>
    /// <param name="amount">What went back, inclusive of tax.</param>
    /// <param name="mode">Where it went.</param>
    /// <param name="refundId">The Payments module's refund, when it went to the original instrument.</param>
    public void Settle(decimal amount, ReturnRefundMode mode, Guid? refundId)
    {
        RefundAmount = Guard.NotNegative(amount);
        RefundMode = mode;
        RefundId = refundId;
    }

    /// <summary>Records the credit note raised for it.</summary>
    /// <param name="creditNoteId">The credit note.</param>
    public void AttachCreditNote(Guid creditNoteId) => CreditNoteId = creditNoteId;

    /// <summary>Records the replacement order placed for it.</summary>
    /// <param name="orderId">The replacement order.</param>
    public void AttachReplacement(Guid orderId) => ReplacementOrderId = orderId;
}
