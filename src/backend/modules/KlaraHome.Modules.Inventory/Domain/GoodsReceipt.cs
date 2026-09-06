using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Inventory.Domain;

/// <summary>Where a goods receipt is in its short life.</summary>
internal enum GoodsReceiptStatus
{
    /// <summary>Counted but not yet booked in. Nothing has moved.</summary>
    Draft = 0,

    /// <summary>Booked in. The ledger entries exist and the stock is on the shelf.</summary>
    Posted = 1,
}

/// <summary>
/// The note recording what actually turned up against a purchase order — a GRN
/// (docs/03-database-design.md §4.5).
/// </summary>
/// <remarks>
/// <para>
/// This is the document that moves stock, and the split from the purchase order is the point: what
/// was ordered and what arrived are different facts, and a system that stores only one of them
/// cannot tell a buyer their supplier is short-shipping them.
/// </para>
/// <para>
/// Posting is one-way and one-time. A receipt that could be posted twice would double the stock,
/// and a receipt that could be un-posted would leave a ledger entry pointing at a document that
/// denies it — so a correction to a posted receipt is a new adjustment, with a reason.
/// </para>
/// </remarks>
internal sealed class GoodsReceipt : AggregateRoot<Guid>, ITenantScoped, IAuditable, IVendorScoped
{
    private readonly List<GoodsReceiptLine> _lines = [];

    private GoodsReceipt(Guid id, string number, Guid purchaseOrderId, Guid warehouseId, Guid? vendorId)
        : base(id)
    {
        Number = Guard.NotNullOrWhiteSpace(number);
        PurchaseOrderId = Guard.NotEmpty(purchaseOrderId);
        WarehouseId = Guard.NotEmpty(warehouseId);
        VendorId = vendorId;
        Status = GoodsReceiptStatus.Draft;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private GoodsReceipt() => Number = string.Empty;

    /// <summary>The human-quotable document number: <c>GRN-000017</c>.</summary>
    public string Number { get; private set; }

    /// <summary>The order the goods came against.</summary>
    public Guid PurchaseOrderId { get; private set; }

    /// <summary>Where they were received.</summary>
    public Guid WarehouseId { get; private set; }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>Whether the stock has been booked in.</summary>
    public GoodsReceiptStatus Status { get; private set; }

    /// <summary>When the goods arrived.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Who took delivery.</summary>
    public Guid? ReceivedBy { get; private set; }

    /// <summary>Anything the receiver wrote on it.</summary>
    public string? Notes { get; private set; }

    /// <summary>What arrived, line by line.</summary>
    public IReadOnlyList<GoodsReceiptLine> Lines => _lines;

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

    /// <summary>Opens a receipt against an order.</summary>
    /// <param name="number">The document number.</param>
    /// <param name="purchaseOrderId">The order the goods came against.</param>
    /// <param name="warehouseId">Where they were received.</param>
    /// <param name="vendorId">The seller, or null for the platform.</param>
    /// <param name="receivedAt">When they arrived.</param>
    /// <param name="receivedBy">Who took delivery.</param>
    public static GoodsReceipt Open(
        string number,
        Guid purchaseOrderId,
        Guid warehouseId,
        Guid? vendorId,
        DateTimeOffset receivedAt,
        Guid? receivedBy)
        => new(UuidV7.New(), number, purchaseOrderId, warehouseId, vendorId)
        {
            ReceivedAt = receivedAt,
            ReceivedBy = receivedBy,
        };

    /// <summary>Records what turned up.</summary>
    /// <param name="lines">The counted lines.</param>
    /// <param name="notes">Anything the receiver wrote.</param>
    public void Record(IEnumerable<GoodsReceiptLine> lines, string? notes)
    {
        ArgumentNullException.ThrowIfNull(lines);

        _lines.Clear();
        _lines.AddRange(lines);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    /// <summary>Marks the receipt booked in. Returns false if it already was.</summary>
    /// <param name="at">When.</param>
    public bool Post(DateTimeOffset at)
    {
        if (Status == GoodsReceiptStatus.Posted)
        {
            return false;
        }

        Status = GoodsReceiptStatus.Posted;
        ReceivedAt = ReceivedAt == default ? at : ReceivedAt;
        return true;
    }
}

/// <summary>
/// One line of a goods receipt: what arrived, what was refused, and which lot it was
/// (docs/03-database-design.md §4.5).
/// </summary>
/// <remarks>
/// Rejected units are recorded and deliberately <em>not</em> booked in. They are the supplier's
/// problem, they never reach the shelf, and the count is what a buyer quotes back to them — a
/// receipt that stored only the accepted quantity would silently lose the evidence.
/// </remarks>
internal sealed class GoodsReceiptLine : Entity<Guid>, ITenantScoped
{
    private GoodsReceiptLine(
        Guid id,
        Guid goodsReceiptId,
        Guid purchaseOrderLineId,
        Guid stockItemId,
        int quantityAccepted,
        int quantityRejected)
        : base(id)
    {
        GoodsReceiptId = goodsReceiptId;
        PurchaseOrderLineId = Guard.NotEmpty(purchaseOrderLineId);
        StockItemId = Guard.NotEmpty(stockItemId);
        QuantityAccepted = quantityAccepted;
        QuantityRejected = quantityRejected;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private GoodsReceiptLine()
    {
    }

    /// <summary>The receipt this line belongs to.</summary>
    public Guid GoodsReceiptId { get; private set; }

    /// <summary>The ordered line it satisfies.</summary>
    public Guid PurchaseOrderLineId { get; private set; }

    /// <summary>The stock row the accepted units were booked into.</summary>
    public Guid StockItemId { get; private set; }

    /// <summary>How many were accepted and booked in.</summary>
    public int QuantityAccepted { get; private set; }

    /// <summary>How many were refused at the door.</summary>
    public int QuantityRejected { get; private set; }

    /// <summary>Why they were refused.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>The supplier's lot number, for a batch-tracked item.</summary>
    public string? BatchCode { get; private set; }

    /// <summary>When that lot expires.</summary>
    public DateOnly? ExpiresOn { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Records one counted line.</summary>
    /// <param name="goodsReceiptId">The receipt.</param>
    /// <param name="purchaseOrderLineId">The ordered line it satisfies.</param>
    /// <param name="stockItemId">The stock row the accepted units go into.</param>
    /// <param name="quantityAccepted">How many were accepted.</param>
    /// <param name="quantityRejected">How many were refused.</param>
    /// <param name="rejectionReason">Why they were refused.</param>
    /// <param name="batchCode">The supplier's lot number.</param>
    /// <param name="expiresOn">When that lot expires.</param>
    public static GoodsReceiptLine Record(
        Guid goodsReceiptId,
        Guid purchaseOrderLineId,
        Guid stockItemId,
        int quantityAccepted,
        int quantityRejected,
        string? rejectionReason,
        string? batchCode,
        DateOnly? expiresOn)
        => new(
            UuidV7.New(),
            goodsReceiptId,
            purchaseOrderLineId,
            stockItemId,
            Math.Max(0, quantityAccepted),
            Math.Max(0, quantityRejected))
        {
            RejectionReason = string.IsNullOrWhiteSpace(rejectionReason) ? null : rejectionReason.Trim(),
            BatchCode = string.IsNullOrWhiteSpace(batchCode) ? null : batchCode.Trim().ToUpperInvariant(),
            ExpiresOn = expiresOn,
        };
}
