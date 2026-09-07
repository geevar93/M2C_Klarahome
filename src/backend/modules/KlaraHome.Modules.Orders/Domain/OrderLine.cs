using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Orders.Domain;

/// <summary>
/// What was bought, as it was described when it was bought (docs/03-database-design.md §4.8).
/// Stored as <c>jsonb</c>.
/// </summary>
/// <remarks>
/// The catalogue is free to be edited, renamed, re-photographed and re-categorised, and none of that
/// may reach an order that has already been placed. Everything a shopper, an invoice or a courier
/// manifest has to show about the item is copied here at placement and never read from Catalog
/// again (docs/02-domain-model.md §4.4).
/// </remarks>
internal sealed class ProductSnapshot
{
    /// <summary>The variant's full display name, product name included.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The product the variant belongs to.</summary>
    public Guid ProductId { get; set; }

    /// <summary>The category it sat in, for reporting and for the commission that was resolved.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Its brand, when it had one.</summary>
    public Guid? BrandId { get; set; }

    /// <summary>The HSN code the GST rate was resolved from. Printed on the invoice.</summary>
    public string? HsnCode { get; set; }

    /// <summary>The image an order line renders. A media file id, resolved for display only.</summary>
    public Guid? ImageFileId { get; set; }

    /// <summary>Dead weight in grams, for a courier manifest.</summary>
    public int WeightGrams { get; set; }

    /// <summary>Whether the product could be returned at all, as its policy stood.</summary>
    public bool IsReturnable { get; set; }

    /// <summary>The product's own return window in days, or null to use the store's.</summary>
    public int? ReturnWindowDays { get; set; }
}

/// <summary>Where one line stands, when it differs from its sub-order's.</summary>
/// <remarks>
/// A line has a status of its own because a partial cancellation and a partial return are both
/// per-line: four of five units going back leaves a sub-order that is neither cancelled nor whole,
/// and only the line can say which part is which.
/// </remarks>
internal enum OrderLineStatus
{
    /// <summary>Everything on it is still expected.</summary>
    Active = 0,

    /// <summary>Some units have been cancelled; the rest stand.</summary>
    PartiallyCancelled = 1,

    /// <summary>Every unit has been cancelled.</summary>
    Cancelled = 2,

    /// <summary>Some units have come back.</summary>
    PartiallyReturned = 3,

    /// <summary>Every unit has come back.</summary>
    Returned = 4,
}

/// <summary>
/// One item on one seller's part of an order (docs/03-database-design.md §4.8).
/// </summary>
/// <remarks>
/// <para>
/// Every money figure on it is frozen at placement, straight from the quote the shopper agreed to.
/// Nothing here is recomputed — not the tax, not the discount allocation, not the line total —
/// because a second calculation is how an invoice and a settlement come to disagree about what a
/// customer paid (docs/02-domain-model.md §4.4).
/// </para>
/// <para>
/// The commission is frozen too, and for a stronger reason: it is what the platform will charge this
/// seller for this sale, and re-resolving it at settlement would let a plan changed in June alter
/// what was earned in April.
/// </para>
/// </remarks>
internal sealed class OrderLine : Entity<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private OrderLine(Guid id, Guid subOrderId, Guid vendorId, Guid listingId, string sku, int quantity)
        : base(id)
    {
        SubOrderId = subOrderId;
        VendorId = vendorId;
        ListingId = listingId;
        Sku = sku;
        Quantity = quantity;
        Status = OrderLineStatus.Active;
        Snapshot = new ProductSnapshot();
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private OrderLine()
    {
        Sku = string.Empty;
        Snapshot = new ProductSnapshot();
    }

    /// <summary>The seller's part it belongs to.</summary>
    public Guid SubOrderId { get; private set; }

    /// <summary>The seller, copied down from the sub-order so the vendor filter reaches the line.</summary>
    public Guid? VendorId { get; private set; }

    /// <summary>The offer that was bought.</summary>
    public Guid ListingId { get; private set; }

    /// <summary>The sellable thing behind the offer.</summary>
    public Guid VariantId { get; private set; }

    /// <summary>
    /// The stock location the units were taken from, recorded at placement.
    /// </summary>
    /// <remarks>
    /// Denormalised onto the line rather than looked up through the reservation, because it is a
    /// fact about the order rather than about a hold: holds are swept, and the answer to "which
    /// shelf did this parcel come off" has to outlive them. Null on a line placed before this was
    /// recorded, and on one for an offer nobody stocks (docs/03-database-design.md §4.5).
    /// </remarks>
    public Guid? WarehouseId { get; private set; }

    /// <summary>The stock-keeping unit, frozen.</summary>
    public string Sku { get; private set; }

    /// <summary>What the item was, as it was described at the time.</summary>
    public ProductSnapshot Snapshot { get; private set; }

    /// <summary>How many units were ordered. Never changes; cancellations are counted separately.</summary>
    public int Quantity { get; private set; }

    /// <summary>Maximum retail price per unit. Statutory in India, and printed.</summary>
    public decimal UnitMrp { get; private set; }

    /// <summary>Selling price per unit, inclusive of GST, before discount.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>Every discount on this line, its share of an order-level one included.</summary>
    public decimal DiscountAmount { get; private set; }

    /// <summary>The gross less discount, with the tax taken back out of it.</summary>
    public decimal TaxableValue { get; private set; }

    /// <summary>The GST percentage applied.</summary>
    public decimal GstRate { get; private set; }

    /// <summary>Central GST. Zero on an inter-state supply.</summary>
    public decimal Cgst { get; private set; }

    /// <summary>State GST. Zero on an inter-state supply.</summary>
    public decimal Sgst { get; private set; }

    /// <summary>Integrated GST. Zero on an intra-state supply.</summary>
    public decimal Igst { get; private set; }

    /// <summary>Compensation cess.</summary>
    public decimal Cess { get; private set; }

    /// <summary>What the shopper pays for this line: gross less discount.</summary>
    public decimal LineTotal { get; private set; }

    /// <summary>The commission percentage the platform charges on it, resolved at placement.</summary>
    public decimal CommissionRate { get; private set; }

    /// <summary>What that comes to in money, resolved at placement.</summary>
    public decimal CommissionAmount { get; private set; }

    /// <summary>The commission plan that produced those two figures, for a seller who asks why.</summary>
    public Guid? CommissionPlanId { get; private set; }

    /// <summary>Where the line stands.</summary>
    public OrderLineStatus Status { get; private set; }

    /// <summary>How many units have been cancelled.</summary>
    public int QuantityCancelled { get; private set; }

    /// <summary>How many units have come back.</summary>
    public int QuantityReturned { get; private set; }

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

    /// <summary>Units still expected: ordered, less cancelled.</summary>
    public int QuantityLive => Quantity - QuantityCancelled;

    /// <summary>Whether nothing on this line survives.</summary>
    public bool IsFullyCancelled => QuantityCancelled >= Quantity;

    /// <summary>
    /// What the cancelled units are worth, at the line's own frozen unit economics.
    /// </summary>
    /// <remarks>
    /// A pro-rata share of the line total rather than <c>quantity × unit price</c>: the line total is
    /// already net of a discount that may have been allocated to it from the order, and charging the
    /// undiscounted price back would refund a shopper less than they paid.
    /// </remarks>
    public decimal CancelledValue
        => Quantity <= 0 || QuantityCancelled <= 0
            ? 0m
            : Math.Round(LineTotal * QuantityCancelled / Quantity, 4, MidpointRounding.AwayFromZero);

    /// <summary>Opens a line.</summary>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="vendorId">The seller.</param>
    /// <param name="listingId">The offer bought.</param>
    /// <param name="sku">The stock-keeping unit.</param>
    /// <param name="quantity">How many units.</param>
    public static OrderLine Create(Guid subOrderId, Guid vendorId, Guid listingId, string sku, int quantity)
        => new(
            UuidV7.New(),
            Guard.NotEmpty(subOrderId),
            Guard.NotEmpty(vendorId),
            Guard.NotEmpty(listingId),
            Guard.NotNullOrWhiteSpace(sku),
            Guard.Positive(quantity));

    /// <summary>Records where the units were allocated from.</summary>
    /// <param name="warehouseId">The stock location, or null when the offer is not stocked.</param>
    public void AllocateFrom(Guid? warehouseId) => WarehouseId = warehouseId;

    /// <summary>Freezes what the item was.</summary>
    /// <param name="variantId">The sellable thing.</param>
    /// <param name="snapshot">How it was described.</param>
    public void Capture(Guid variantId, ProductSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        VariantId = variantId;
        Snapshot = snapshot;
    }

    /// <summary>Freezes the agreed money, straight from the quote.</summary>
    /// <param name="unitMrp">Maximum retail price per unit.</param>
    /// <param name="unitPrice">Selling price per unit, inclusive of GST.</param>
    /// <param name="discountAmount">Every discount on the line.</param>
    /// <param name="taxableValue">What the tax was computed on.</param>
    /// <param name="gstRate">The GST percentage.</param>
    /// <param name="cgst">Central GST.</param>
    /// <param name="sgst">State GST.</param>
    /// <param name="igst">Integrated GST.</param>
    /// <param name="cess">Compensation cess.</param>
    /// <param name="lineTotal">What the shopper pays for the line.</param>
    public void Price(
        decimal unitMrp,
        decimal unitPrice,
        decimal discountAmount,
        decimal taxableValue,
        decimal gstRate,
        decimal cgst,
        decimal sgst,
        decimal igst,
        decimal cess,
        decimal lineTotal)
    {
        UnitMrp = unitMrp;
        UnitPrice = unitPrice;
        DiscountAmount = discountAmount;
        TaxableValue = taxableValue;
        GstRate = gstRate;
        Cgst = cgst;
        Sgst = sgst;
        Igst = igst;
        Cess = cess;
        LineTotal = lineTotal;
    }

    /// <summary>Freezes what the platform charges the seller for this sale.</summary>
    /// <param name="planId">The plan that decided it.</param>
    /// <param name="ratePercent">The rate.</param>
    /// <param name="amount">What it comes to.</param>
    public void SetCommission(Guid? planId, decimal ratePercent, decimal amount)
    {
        CommissionPlanId = planId;
        CommissionRate = ratePercent;
        CommissionAmount = amount;
    }

    /// <summary>
    /// Cancels units, up to what is left.
    /// </summary>
    /// <remarks>
    /// Clamped rather than refused, because the two callers differ: a shopper cancelling a whole
    /// sub-order asks for everything on every line, and a shopper cancelling one line asks for a
    /// number they typed. Clamping serves both, and a request for more than remains has the same
    /// meaning as a request for all of it.
    /// </remarks>
    /// <param name="quantity">How many units to cancel.</param>
    /// <returns>How many were actually cancelled by this call.</returns>
    public int Cancel(int quantity)
    {
        var taken = Math.Min(Math.Max(quantity, 0), QuantityLive);

        if (taken == 0)
        {
            return 0;
        }

        QuantityCancelled += taken;
        Status = IsFullyCancelled ? OrderLineStatus.Cancelled : OrderLineStatus.PartiallyCancelled;

        return taken;
    }

    /// <summary>Records units that have come back, up to what is live.</summary>
    /// <param name="quantity">How many units returned.</param>
    /// <returns>How many were actually recorded by this call.</returns>
    public int RecordReturn(int quantity)
    {
        var taken = Math.Min(Math.Max(quantity, 0), QuantityLive - QuantityReturned);

        if (taken == 0)
        {
            return 0;
        }

        QuantityReturned += taken;
        Status = QuantityReturned >= QuantityLive ? OrderLineStatus.Returned : OrderLineStatus.PartiallyReturned;

        return taken;
    }
}
