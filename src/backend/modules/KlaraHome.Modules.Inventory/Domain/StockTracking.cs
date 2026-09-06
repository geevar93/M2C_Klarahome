using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Inventory.Domain;

/// <summary>
/// A lot of units received together, with the dates that came with it
/// (docs/03-database-design.md §4.5).
/// </summary>
/// <remarks>
/// <para>
/// Populated only for a stock item whose <see cref="StockTrackingMode"/> is
/// <see cref="StockTrackingMode.Batch"/>. The table exists for every deployment because the
/// alternative — adding it later, when the first category that needs an expiry date appears — is a
/// migration against a live stock table, and this one costs nothing while it is empty.
/// </para>
/// <para>
/// The batch quantity is deliberately <em>not</em> part of the on-hand invariant. On hand is the
/// ledger sum and nothing else; batches are a breakdown of it, reconciled by the same nightly job
/// that reconciles the caches. Making the ledger depend on them would put a second source of truth
/// next to the first.
/// </para>
/// </remarks>
internal sealed class StockBatch : Entity<Guid>, ITenantScoped, IAuditable
{
    private StockBatch(Guid id, Guid stockItemId, string batchCode, int quantity)
        : base(id)
    {
        StockItemId = Guard.NotEmpty(stockItemId);
        BatchCode = Guard.NotNullOrWhiteSpace(batchCode);
        Quantity = quantity;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private StockBatch() => BatchCode = string.Empty;

    /// <summary>The stock row this lot is part of.</summary>
    public Guid StockItemId { get; private set; }

    /// <summary>The supplier's lot number. Unique within the stock item.</summary>
    public string BatchCode { get; private set; }

    /// <summary>How many units of this lot are still held.</summary>
    public int Quantity { get; private set; }

    /// <summary>When the lot was made.</summary>
    public DateOnly? ManufacturedOn { get; private set; }

    /// <summary>When it expires. What the whole table is for.</summary>
    public DateOnly? ExpiresOn { get; private set; }

    /// <summary>Who it came from.</summary>
    public Guid? SupplierId { get; private set; }

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

    /// <summary>Records a lot.</summary>
    /// <param name="stockItemId">The stock row.</param>
    /// <param name="batchCode">The supplier's lot number.</param>
    /// <param name="quantity">How many units.</param>
    /// <param name="manufacturedOn">When it was made.</param>
    /// <param name="expiresOn">When it expires.</param>
    /// <param name="supplierId">Who it came from.</param>
    public static StockBatch Record(
        Guid stockItemId,
        string batchCode,
        int quantity,
        DateOnly? manufacturedOn,
        DateOnly? expiresOn,
        Guid? supplierId)
        => new(UuidV7.New(), stockItemId, batchCode.Trim().ToUpperInvariant(), Math.Max(0, quantity))
        {
            ManufacturedOn = manufacturedOn,
            ExpiresOn = expiresOn,
            SupplierId = supplierId,
        };

    /// <summary>Adds units to an existing lot, as a second delivery of the same batch would.</summary>
    /// <param name="quantity">How many more.</param>
    public void Add(int quantity) => Quantity = Math.Max(0, Quantity + quantity);

    /// <summary>Restates the lot's dates.</summary>
    /// <param name="manufacturedOn">When it was made.</param>
    /// <param name="expiresOn">When it expires.</param>
    public void Redate(DateOnly? manufacturedOn, DateOnly? expiresOn)
    {
        ManufacturedOn = manufacturedOn;
        ExpiresOn = expiresOn;
    }
}

/// <summary>Where an individually tracked unit is.</summary>
internal enum SerialStatus
{
    /// <summary>On the shelf and sellable.</summary>
    InStock = 0,

    /// <summary>Allocated to a cart or an order.</summary>
    Reserved = 1,

    /// <summary>Sold and gone.</summary>
    Sold = 2,

    /// <summary>Came back from a customer.</summary>
    Returned = 3,

    /// <summary>Written off.</summary>
    Damaged = 4,
}

/// <summary>
/// One individually identified unit (docs/03-database-design.md §4.5).
/// </summary>
/// <remarks>
/// Populated only for a stock item whose <see cref="StockTrackingMode"/> is
/// <see cref="StockTrackingMode.Serial"/> — anything with a warranty, a registration or an IMEI. As
/// with batches, the count here is a breakdown of on hand rather than a second source of it.
/// </remarks>
internal sealed class StockSerial : Entity<Guid>, ITenantScoped, IAuditable
{
    private StockSerial(Guid id, Guid stockItemId, string serialNumber)
        : base(id)
    {
        StockItemId = Guard.NotEmpty(stockItemId);
        SerialNumber = Guard.NotNullOrWhiteSpace(serialNumber);
        Status = SerialStatus.InStock;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private StockSerial() => SerialNumber = string.Empty;

    /// <summary>The stock row this unit is part of.</summary>
    public Guid StockItemId { get; private set; }

    /// <summary>The manufacturer's number. Unique within the stock item.</summary>
    public string SerialNumber { get; private set; }

    /// <summary>The lot it came in, when the item is both batch- and serial-tracked.</summary>
    public Guid? BatchId { get; private set; }

    /// <summary>Where the unit is.</summary>
    public SerialStatus Status { get; private set; }

    /// <summary>The cart, order or return that last moved it.</summary>
    public Guid? ReferenceId { get; private set; }

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

    /// <summary>Books a unit in.</summary>
    /// <param name="stockItemId">The stock row.</param>
    /// <param name="serialNumber">The manufacturer's number.</param>
    /// <param name="batchId">The lot it came in.</param>
    public static StockSerial Record(Guid stockItemId, string serialNumber, Guid? batchId)
        => new(UuidV7.New(), stockItemId, serialNumber.Trim().ToUpperInvariant())
        {
            BatchId = batchId,
        };

    /// <summary>Moves the unit.</summary>
    /// <param name="status">Where it is now.</param>
    /// <param name="referenceId">What moved it.</param>
    public void MoveTo(SerialStatus status, Guid? referenceId)
    {
        Status = status;
        ReferenceId = referenceId;
    }
}
