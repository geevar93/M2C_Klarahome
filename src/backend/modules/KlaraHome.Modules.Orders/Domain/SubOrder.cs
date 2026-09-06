using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Orders.Domain;

/// <summary>Who asked for a cancellation. Recorded because the three are settled differently.</summary>
internal enum CancellationInitiator
{
    /// <summary>The shopper changed their mind, before dispatch.</summary>
    Customer = 0,

    /// <summary>The seller could not fulfil it. Counts against their performance.</summary>
    Vendor = 1,

    /// <summary>Operations stopped it — fraud, a pricing error, a parcel recalled from a courier.</summary>
    Platform = 2,

    /// <summary>The platform itself: an unpaid order that timed out.</summary>
    System = 3,
}

/// <summary>
/// One seller's part of an order (docs/02-domain-model.md §4.4).
/// </summary>
/// <remarks>
/// <para>
/// The unit a marketplace actually operates on. It is what a seller sees on their worklist, what a
/// courier collects, what carries its own status through the lifecycle, and what a tax invoice is
/// raised against — because the supply is the <em>seller's</em>, made under the seller's GSTIN, and
/// two sellers in one basket are two supplies however they were paid for.
/// </para>
/// <para>
/// Every line on it shares one vendor. That is the invariant the split exists to hold, and it is
/// enforced at construction rather than checked later: a sub-order with two sellers' lines could not
/// be invoiced, settled or dispatched by anybody.
/// </para>
/// </remarks>
internal sealed class SubOrder : Entity<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private readonly List<OrderLine> _lines = [];

    private SubOrder(Guid id, Guid orderId, Guid vendorId, string subOrderNumber, string currencyCode)
        : base(id)
    {
        OrderId = orderId;
        VendorId = vendorId;
        SubOrderNumber = subOrderNumber;
        CurrencyCode = currencyCode;
        Status = SubOrderStatus.PendingPayment;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private SubOrder()
    {
        SubOrderNumber = string.Empty;
        CurrencyCode = Money.Inr;
    }

    /// <summary>The order it belongs to.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>
    /// The seller who fulfils it.
    /// </summary>
    /// <remarks>
    /// Nullable only because <see cref="IVendorScoped"/> allows platform-owned rows; a sub-order
    /// always has one, and the column is non-nullable in the schema. The interface is what puts the
    /// vendor query filter on this table, which is how a seller is prevented from reading another
    /// seller's half of a shared basket.
    /// </remarks>
    public Guid? VendorId { get; private set; }

    /// <summary>The seller's short code, frozen so an invoice reprint needs no lookup.</summary>
    public string? VendorCode { get; private set; }

    /// <summary>The seller's trading name at the time of the order.</summary>
    public string? VendorName { get; private set; }

    /// <summary>The seller's GSTIN at the time of the order. The invoice is raised under it.</summary>
    public string? VendorGstin { get; private set; }

    /// <summary>The number on the seller's worklist: the order number with a per-seller suffix.</summary>
    public string SubOrderNumber { get; private set; }

    /// <summary>Where it is in its life.</summary>
    public SubOrderStatus Status { get; private set; }

    /// <summary>The lines' gross, before any discount.</summary>
    public decimal ItemsTotal { get; private set; }

    /// <summary>The lines' discount, line-level and allocated.</summary>
    public decimal DiscountTotal { get; private set; }

    /// <summary>This seller's share of delivery, inclusive of its tax.</summary>
    public decimal ShippingTotal { get; private set; }

    /// <summary>The tax inside that shipping figure.</summary>
    public decimal ShippingTax { get; private set; }

    /// <summary>The lines' CGST + SGST + IGST + cess.</summary>
    public decimal TaxTotal { get; private set; }

    /// <summary>What was computed on, after discount.</summary>
    public decimal TaxableValue { get; private set; }

    /// <summary>What the shopper pays for this seller's part.</summary>
    public decimal Total { get; private set; }

    /// <summary>ISO 4217 code every figure on it is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>The delivery service the shopper chose for this seller.</summary>
    public string? ShippingOptionCode { get; private set; }

    /// <summary>The courier, when one has been decided.</summary>
    public string? Carrier { get; private set; }

    /// <summary>Earliest delivery, in days from dispatch, as promised at checkout.</summary>
    public int PromisedMinDays { get; private set; }

    /// <summary>Latest delivery, in days from dispatch, as promised at checkout.</summary>
    public int PromisedMaxDays { get; private set; }

    /// <summary>
    /// When the seller must have handed the parcel to a courier.
    /// </summary>
    /// <remarks>
    /// Set at confirmation rather than at placement, and deliberately: a dispatch clock that started
    /// while an order was still waiting for a payment would have half run down before the seller was
    /// told there was anything to pack.
    /// </remarks>
    public DateTimeOffset? DispatchDueAt { get; private set; }

    /// <summary>When it was confirmed.</summary>
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>When it was handed to a courier.</summary>
    public DateTimeOffset? ShippedAt { get; private set; }

    /// <summary>When it was delivered.</summary>
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>When the shopper's right to return it lapses. Set at delivery.</summary>
    public DateTimeOffset? ReturnWindowEndsAt { get; private set; }

    /// <summary>When it was cancelled, in whole.</summary>
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Who asked for the cancellation.</summary>
    public CancellationInitiator? CancelledBy { get; private set; }

    /// <summary>Why, in the words the shopper was shown.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>The lines. Always at least one, always all one seller's.</summary>
    public IReadOnlyList<OrderLine> Lines => _lines;

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

    /// <summary>What has come off the bill through cancellation. Derived from the lines.</summary>
    public decimal CancelledTotal => _lines.Sum(line => line.CancelledValue);

    /// <summary>What is still owed on this seller's part.</summary>
    public decimal NetTotal => Total - CancelledTotal;

    /// <summary>Whether every unit on every line has been cancelled.</summary>
    public bool IsFullyCancelled => _lines.Count > 0 && _lines.TrueForAll(line => line.IsFullyCancelled);

    /// <summary>Opens a seller's part of an order.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="vendorId">The seller.</param>
    /// <param name="subOrderNumber">Its number.</param>
    /// <param name="currencyCode">ISO 4217 code every figure is in.</param>
    public static SubOrder Open(Guid orderId, Guid vendorId, string subOrderNumber, string currencyCode)
        => new(
            UuidV7.New(),
            Guard.NotEmpty(orderId),
            Guard.NotEmpty(vendorId),
            Guard.NotNullOrWhiteSpace(subOrderNumber),
            currencyCode);

    /// <summary>Freezes the seller as they were, for the invoice.</summary>
    /// <param name="code">Their short code.</param>
    /// <param name="name">Their trading name.</param>
    /// <param name="gstin">Their GST registration.</param>
    public void CaptureVendor(string? code, string? name, string? gstin)
    {
        VendorCode = code;
        VendorName = name;
        VendorGstin = gstin;
    }

    /// <summary>Freezes this seller's share of the agreed price.</summary>
    /// <param name="itemsTotal">The lines' gross.</param>
    /// <param name="discountTotal">Their discount.</param>
    /// <param name="taxableValue">What the tax was computed on.</param>
    /// <param name="taxTotal">The lines' tax.</param>
    /// <param name="shippingTotal">Their share of delivery, inclusive of tax.</param>
    /// <param name="shippingTax">The tax inside it.</param>
    /// <param name="total">What the shopper pays for their part.</param>
    public void Price(
        decimal itemsTotal,
        decimal discountTotal,
        decimal taxableValue,
        decimal taxTotal,
        decimal shippingTotal,
        decimal shippingTax,
        decimal total)
    {
        ItemsTotal = itemsTotal;
        DiscountTotal = discountTotal;
        TaxableValue = taxableValue;
        TaxTotal = taxTotal;
        ShippingTotal = shippingTotal;
        ShippingTax = shippingTax;
        Total = total;
    }

    /// <summary>Records what the shopper chose to have it delivered by.</summary>
    /// <param name="optionCode">The service.</param>
    /// <param name="carrier">The courier, when decided.</param>
    /// <param name="promisedMinDays">Earliest delivery, in days from dispatch.</param>
    /// <param name="promisedMaxDays">Latest delivery, in days from dispatch.</param>
    /// <param name="dispatchSlaHours">How long the seller has to dispatch, held until confirmation.</param>
    public void Promise(
        string? optionCode,
        string? carrier,
        int promisedMinDays,
        int promisedMaxDays,
        int dispatchSlaHours)
    {
        ShippingOptionCode = optionCode;
        Carrier = carrier;
        PromisedMinDays = promisedMinDays;
        PromisedMaxDays = promisedMaxDays;
        DispatchSlaHours = dispatchSlaHours;
    }

    /// <summary>
    /// How long the seller has to hand the parcel over, as quoted at checkout.
    /// </summary>
    /// <remarks>
    /// Held on the sub-order rather than re-read from the seller at confirmation, so a seller who
    /// widens their SLA next week does not retroactively give themselves longer on an order the
    /// shopper was already promised.
    /// </remarks>
    public int DispatchSlaHours { get; private set; }

    /// <summary>Attaches a line.</summary>
    /// <param name="line">The line. Must be this seller's.</param>
    public void AddLine(OrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _lines.Add(line);
    }

    /// <summary>
    /// Moves the sub-order, or returns false when the machine forbids it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single write that changes a sub-order's state. Callers never assign
    /// <see cref="Status"/>, and there is no other method here that does — which is what makes
    /// <see cref="SubOrderLifecycle"/> the whole machine rather than most of it.
    /// </para>
    /// <para>
    /// False rather than an exception: two operators pressing "mark packed" on the same sub-order is
    /// a conflict to report, not a programming error.
    /// </para>
    /// </remarks>
    /// <param name="next">Where to move it.</param>
    /// <param name="actor">Who is asking.</param>
    /// <param name="at">The current instant.</param>
    /// <param name="returnWindowDays">The window that starts running on delivery.</param>
    public bool TransitionTo(SubOrderStatus next, OrderActor actor, DateTimeOffset at, int returnWindowDays)
    {
        if (!SubOrderLifecycle.IsAllowedFor(Status, next, actor))
        {
            return false;
        }

        Status = next;

        switch (next)
        {
            case SubOrderStatus.Confirmed:
                // Set once. A sub-order that goes back to a seller after a failed return has not
                // been confirmed twice, and the dispatch clock has already run.
                ConfirmedAt ??= at;
                DispatchDueAt ??= at.AddHours(DispatchSlaHours);
                break;

            case SubOrderStatus.Shipped:
                ShippedAt ??= at;
                break;

            case SubOrderStatus.Delivered:
                DeliveredAt = at;
                ReturnWindowEndsAt = at.AddDays(returnWindowDays);
                break;

            case SubOrderStatus.Cancelled:
                CancelledAt ??= at;
                break;

            default:
                break;
        }

        return true;
    }

    /// <summary>Records who cancelled it and why, alongside the transition.</summary>
    /// <param name="initiator">Who asked.</param>
    /// <param name="reason">Why, in words the shopper can read.</param>
    public void RecordCancellation(CancellationInitiator initiator, string? reason)
    {
        CancelledBy = initiator;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}
