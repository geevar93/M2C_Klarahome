using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Inventory.Domain;

/// <summary>Where a stock take is in its life.</summary>
internal enum StockTakeStatus
{
    /// <summary>Scoped but not started. The expected quantities have not been frozen yet.</summary>
    Draft = 0,

    /// <summary>Counting is under way. Expected quantities are frozen; counts may be entered.</summary>
    Counting = 1,

    /// <summary>Submitted. The variances have been posted to the ledger. Terminal.</summary>
    Submitted = 2,

    /// <summary>Abandoned without posting anything. Terminal.</summary>
    Cancelled = 3,
}

/// <summary>
/// A physical recount of a location, whole or partial (docs/03-database-design.md §4.5).
/// </summary>
/// <remarks>
/// <para>
/// The expected quantity is frozen when counting opens, not read at submission. A count taken on
/// Tuesday and submitted on Thursday must be compared against Tuesday's book figure, or every sale
/// in between becomes a phantom variance and the recount corrects a discrepancy that never existed.
/// </para>
/// <para>
/// Submitting posts one <see cref="StockMovementReason.Correction"/> entry per non-zero variance.
/// That is the only sanctioned way an operator changes on-hand without a movement behind it, and it
/// is deliberately a document rather than a button: a correction with a count sheet attached is
/// auditable, and a bare adjustment is not.
/// </para>
/// </remarks>
internal sealed class StockTake : AggregateRoot<Guid>, ITenantScoped, IAuditable, IVendorScoped
{
    private readonly List<StockTakeLine> _lines = [];

    private StockTake(Guid id, string number, Guid warehouseId, Guid? vendorId)
        : base(id)
    {
        Number = Guard.NotNullOrWhiteSpace(number);
        WarehouseId = Guard.NotEmpty(warehouseId);
        VendorId = vendorId;
        Status = StockTakeStatus.Draft;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private StockTake() => Number = string.Empty;

    /// <summary>The human-quotable document number: <c>STK-000017</c>.</summary>
    public string Number { get; private set; }

    /// <summary>The location being counted.</summary>
    public Guid WarehouseId { get; private set; }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>Where it is in its life.</summary>
    public StockTakeStatus Status { get; private set; }

    /// <summary>When the count is to be taken.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>When the variances were posted.</summary>
    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>Who submitted it.</summary>
    public Guid? SubmittedBy { get; private set; }

    /// <summary>Anything the counter wrote.</summary>
    public string? Notes { get; private set; }

    /// <summary>The counted rows.</summary>
    public IReadOnlyList<StockTakeLine> Lines => _lines;

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

    /// <summary>Whether counts may still be entered.</summary>
    public bool IsOpen => Status is StockTakeStatus.Draft or StockTakeStatus.Counting;

    /// <summary>Schedules a count.</summary>
    /// <param name="number">The document number.</param>
    /// <param name="warehouseId">The location being counted.</param>
    /// <param name="vendorId">The seller, or null for the platform.</param>
    /// <param name="scheduledFor">When the count is to be taken.</param>
    /// <param name="notes">Anything the planner wrote.</param>
    public static StockTake Schedule(
        string number,
        Guid warehouseId,
        Guid? vendorId,
        DateTimeOffset? scheduledFor,
        string? notes)
        => new(UuidV7.New(), number, warehouseId, vendorId)
        {
            ScheduledFor = scheduledFor,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };

    /// <summary>
    /// Opens counting with the book figures frozen onto the sheet. Returns false unless the take is
    /// still a draft.
    /// </summary>
    /// <param name="lines">One line per stock row in scope, carrying today's book figure.</param>
    public bool BeginCounting(IEnumerable<StockTakeLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (Status != StockTakeStatus.Draft)
        {
            return false;
        }

        _lines.Clear();
        _lines.AddRange(lines);
        Status = StockTakeStatus.Counting;
        return true;
    }

    /// <summary>Marks the sheet posted. Returns false unless counting was under way.</summary>
    /// <param name="at">When.</param>
    /// <param name="by">Who.</param>
    public bool Submit(DateTimeOffset at, Guid? by)
    {
        if (Status != StockTakeStatus.Counting)
        {
            return false;
        }

        Status = StockTakeStatus.Submitted;
        SubmittedAt = at;
        SubmittedBy = by;
        return true;
    }

    /// <summary>Abandons the count without posting anything. Returns false if it is already closed.</summary>
    public bool Cancel()
    {
        if (!IsOpen)
        {
            return false;
        }

        Status = StockTakeStatus.Cancelled;
        return true;
    }
}

/// <summary>One counted row of a stock take (docs/03-database-design.md §4.5).</summary>
internal sealed class StockTakeLine : Entity<Guid>, ITenantScoped
{
    private StockTakeLine(Guid id, Guid stockTakeId, Guid stockItemId, string sku, int expectedQuantity)
        : base(id)
    {
        StockTakeId = stockTakeId;
        StockItemId = Guard.NotEmpty(stockItemId);
        Sku = Guard.NotNullOrWhiteSpace(sku);
        ExpectedQuantity = expectedQuantity;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private StockTakeLine() => Sku = string.Empty;

    /// <summary>The sheet this line belongs to.</summary>
    public Guid StockTakeId { get; private set; }

    /// <summary>The stock row being counted.</summary>
    public Guid StockItemId { get; private set; }

    /// <summary>The SKU, so the sheet is countable by a person holding it.</summary>
    public string Sku { get; private set; }

    /// <summary>The book figure, frozen when counting opened.</summary>
    public int ExpectedQuantity { get; private set; }

    /// <summary>What the counter found, or null if this row has not been counted yet.</summary>
    public int? CountedQuantity { get; private set; }

    /// <summary>Counted less expected. Null until the row is counted.</summary>
    public int? Variance => CountedQuantity - ExpectedQuantity;

    /// <summary>What the counter wrote about this row.</summary>
    public string? Note { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Puts a row on the sheet with today's book figure.</summary>
    /// <param name="stockTakeId">The sheet.</param>
    /// <param name="stockItemId">The stock row.</param>
    /// <param name="sku">The SKU.</param>
    /// <param name="expectedQuantity">The book figure.</param>
    public static StockTakeLine Expect(Guid stockTakeId, Guid stockItemId, string sku, int expectedQuantity)
        => new(UuidV7.New(), stockTakeId, stockItemId, sku, expectedQuantity);

    /// <summary>Records what the counter found.</summary>
    /// <param name="countedQuantity">The count. Negative counts are refused as zero.</param>
    /// <param name="note">What they wrote.</param>
    public void Count(int countedQuantity, string? note)
    {
        CountedQuantity = Math.Max(0, countedQuantity);
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
