using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Inventory.Domain;

/// <summary>Where a purchase order is in its life.</summary>
internal enum PurchaseOrderStatus
{
    /// <summary>Being written. Lines may still be added and removed.</summary>
    Draft = 0,

    /// <summary>Sent to the supplier. The lines are now a commitment and are frozen.</summary>
    Submitted = 1,

    /// <summary>Some of it has arrived.</summary>
    PartiallyReceived = 2,

    /// <summary>All of it has arrived.</summary>
    Received = 3,

    /// <summary>Called off. Terminal.</summary>
    Cancelled = 4,
}

/// <summary>
/// An order placed on a supplier to replenish stock (docs/03-database-design.md §4.5).
/// </summary>
/// <remarks>
/// <para>
/// Nothing here moves stock. A purchase order is an intention, and intentions do not appear on a
/// shelf; the goods receipt is what writes the <see cref="StockMovementReason.Purchase"/> ledger
/// entries. Keeping those separate is what makes "ordered but not arrived" a number the buyer can
/// see rather than a discrepancy they discover.
/// </para>
/// <para>
/// The lines are frozen at submission. A supplier who has been sent an order and then finds the
/// quantities changed underneath them is a dispute, and the fix for it is that the document is
/// immutable once it has left the building.
/// </para>
/// </remarks>
internal sealed class PurchaseOrder : AggregateRoot<Guid>, ITenantScoped, IAuditable, IVendorScoped
{
    private readonly List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder(Guid id, string number, Guid supplierId, Guid warehouseId, Guid? vendorId)
        : base(id)
    {
        Number = Guard.NotNullOrWhiteSpace(number);
        SupplierId = Guard.NotEmpty(supplierId);
        WarehouseId = Guard.NotEmpty(warehouseId);
        VendorId = vendorId;
        Status = PurchaseOrderStatus.Draft;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private PurchaseOrder() => Number = string.Empty;

    /// <summary>The human-quotable document number: <c>PO-000017</c>.</summary>
    public string Number { get; private set; }

    /// <summary>Who it is placed on.</summary>
    public Guid SupplierId { get; private set; }

    /// <summary>Where the goods are to be delivered.</summary>
    public Guid WarehouseId { get; private set; }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>Where it is in its life.</summary>
    public PurchaseOrderStatus Status { get; private set; }

    /// <summary>When the supplier said it would arrive.</summary>
    public DateTimeOffset? ExpectedAt { get; private set; }

    /// <summary>When it was sent to the supplier.</summary>
    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>The sum of the lines before tax.</summary>
    public Money Subtotal { get; private set; } = Money.Rupees(0m);

    /// <summary>The tax on the lines.</summary>
    public Money TaxTotal { get; private set; } = Money.Rupees(0m);

    /// <summary>What the buyer expects to pay.</summary>
    public Money Total { get; private set; } = Money.Rupees(0m);

    /// <summary>Anything the buyer wrote on it.</summary>
    public string? Notes { get; private set; }

    /// <summary>The lines. Frozen once the order is submitted.</summary>
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

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

    /// <summary>Whether the document may still be edited.</summary>
    public bool IsEditable => Status == PurchaseOrderStatus.Draft;

    /// <summary>Whether goods may still be received against it.</summary>
    public bool IsReceivable
        => Status is PurchaseOrderStatus.Submitted or PurchaseOrderStatus.PartiallyReceived;

    /// <summary>Raises a draft order.</summary>
    /// <param name="number">The document number.</param>
    /// <param name="supplierId">Who it is placed on.</param>
    /// <param name="warehouseId">Where the goods go.</param>
    /// <param name="vendorId">The seller buying, or null for the platform.</param>
    public static PurchaseOrder Raise(string number, Guid supplierId, Guid warehouseId, Guid? vendorId)
        => new(UuidV7.New(), number, supplierId, warehouseId, vendorId);

    /// <summary>Replaces the lines. Only while the order is a draft.</summary>
    /// <param name="lines">The new lines.</param>
    public void SetLines(IEnumerable<PurchaseOrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        _lines.Clear();
        _lines.AddRange(lines);
        Recalculate();
    }

    /// <summary>Restates the delivery expectation and the note.</summary>
    /// <param name="expectedAt">When the supplier said it would arrive.</param>
    /// <param name="notes">Anything the buyer wrote.</param>
    public void Describe(DateTimeOffset? expectedAt, string? notes)
    {
        ExpectedAt = expectedAt;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    /// <summary>Sends it to the supplier. Returns false if it is not a draft, or has no lines.</summary>
    /// <param name="at">When.</param>
    public bool Submit(DateTimeOffset at)
    {
        if (!IsEditable || _lines.Count == 0)
        {
            return false;
        }

        Status = PurchaseOrderStatus.Submitted;
        SubmittedAt = at;
        return true;
    }

    /// <summary>Calls it off. Returns false if goods have already arrived against it.</summary>
    /// <remarks>
    /// A partially received order cannot be cancelled, because the units already on the shelf are
    /// real and the document is the only record of where they came from.
    /// </remarks>
    public bool Cancel()
    {
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted))
        {
            return false;
        }

        Status = PurchaseOrderStatus.Cancelled;
        return true;
    }

    /// <summary>
    /// Recomputes the status from what the lines say has arrived. Called after a receipt is posted.
    /// </summary>
    public void RefreshReceiptStatus()
    {
        if (!IsReceivable)
        {
            return;
        }

        var received = _lines.Sum(line => line.QuantityReceived);

        Status = received switch
        {
            0 => PurchaseOrderStatus.Submitted,
            _ when _lines.TrueForAll(line => line.IsFullyReceived) => PurchaseOrderStatus.Received,
            _ => PurchaseOrderStatus.PartiallyReceived,
        };
    }

    /// <summary>Rolls the line totals up onto the document.</summary>
    private void Recalculate()
    {
        var subtotal = 0m;
        var tax = 0m;

        foreach (var line in _lines)
        {
            subtotal += line.LineTotal.Amount;
            tax += line.TaxAmount;
        }

        Subtotal = Money.Rupees(subtotal);
        TaxTotal = Money.Rupees(tax);
        Total = Money.Rupees(subtotal + tax);
    }
}

/// <summary>
/// One line of a purchase order (docs/03-database-design.md §4.5).
/// </summary>
/// <remarks>
/// It holds a <c>listing_id</c> and a copy of the SKU and description. The copy is what makes the
/// document readable a year later, when the listing may have been renamed or archived: a purchase
/// order is a commercial record, and it has to say what was actually ordered rather than what the
/// catalogue currently calls it.
/// </remarks>
internal sealed class PurchaseOrderLine : Entity<Guid>, ITenantScoped
{
    private PurchaseOrderLine(
        Guid id,
        Guid purchaseOrderId,
        Guid listingId,
        string sku,
        int quantityOrdered,
        Money unitCost,
        decimal taxRate)
        : base(id)
    {
        PurchaseOrderId = purchaseOrderId;
        ListingId = Guard.NotEmpty(listingId);
        Sku = Guard.NotNullOrWhiteSpace(sku);
        QuantityOrdered = Guard.Positive(quantityOrdered);
        UnitCost = unitCost;
        TaxRate = Guard.NotNegative(taxRate);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private PurchaseOrderLine()
    {
        Sku = string.Empty;
        UnitCost = Money.Rupees(0m);
    }

    /// <summary>The document this line belongs to.</summary>
    public Guid PurchaseOrderId { get; private set; }

    /// <summary>The offer being bought in.</summary>
    public Guid ListingId { get; private set; }

    /// <summary>The SKU as it read when the order was raised.</summary>
    public string Sku { get; private set; }

    /// <summary>What it is, as it read when the order was raised.</summary>
    public string? Description { get; private set; }

    /// <summary>How many were ordered.</summary>
    public int QuantityOrdered { get; private set; }

    /// <summary>How many have arrived and been accepted so far.</summary>
    public int QuantityReceived { get; private set; }

    /// <summary>What the supplier charges per unit, before tax.</summary>
    public Money UnitCost { get; private set; }

    /// <summary>The GST percentage on this line, e.g. <c>18.0000</c>.</summary>
    public decimal TaxRate { get; private set; }

    /// <summary>Quantity times unit cost, before tax.</summary>
    public Money LineTotal { get; private set; } = Money.Rupees(0m);

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>The tax on this line, in rupees.</summary>
    public decimal TaxAmount => decimal.Round(LineTotal.Amount * TaxRate / 100m, 4);

    /// <summary>Whether everything ordered has arrived.</summary>
    public bool IsFullyReceived => QuantityReceived >= QuantityOrdered;

    /// <summary>How many are still outstanding.</summary>
    public int QuantityOutstanding => Math.Max(0, QuantityOrdered - QuantityReceived);

    /// <summary>Adds a line to a draft order.</summary>
    /// <param name="purchaseOrderId">The document.</param>
    /// <param name="listingId">The offer being bought in.</param>
    /// <param name="sku">The SKU.</param>
    /// <param name="description">What it is.</param>
    /// <param name="quantityOrdered">How many.</param>
    /// <param name="unitCost">What the supplier charges per unit.</param>
    /// <param name="taxRate">The GST percentage.</param>
    public static PurchaseOrderLine Add(
        Guid purchaseOrderId,
        Guid listingId,
        string sku,
        string? description,
        int quantityOrdered,
        Money unitCost,
        decimal taxRate)
    {
        var line = new PurchaseOrderLine(
            UuidV7.New(),
            purchaseOrderId,
            listingId,
            sku,
            quantityOrdered,
            unitCost,
            taxRate)
        {
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        };

        line.LineTotal = Money.Rupees(unitCost.Amount * quantityOrdered);

        return line;
    }

    /// <summary>
    /// Records units arriving. Clamped to what is still outstanding, so a receipt can never book in
    /// more than was ordered — which is the database constraint restated where the caller can act
    /// on it.
    /// </summary>
    /// <param name="quantity">How many arrived and were accepted.</param>
    /// <returns>How many were actually booked in.</returns>
    public int Receive(int quantity)
    {
        var accepted = Math.Clamp(quantity, 0, QuantityOutstanding);
        QuantityReceived += accepted;
        return accepted;
    }
}
