using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Inventory.Domain;

/// <summary>Why stock moved (docs/03-database-design.md §4.5).</summary>
/// <remarks>
/// A closed list, and the reason every movement is answerable months later. A reason of
/// <see cref="Correction"/> is the only sanctioned way an operator changes on-hand without a
/// physical movement behind it, and it exists so that "somebody adjusted this" and "we recounted
/// and it was wrong" are not the same row.
/// </remarks>
internal enum StockMovementReason
{
    /// <summary>Goods received against a purchase order.</summary>
    Purchase = 0,

    /// <summary>A reservation was committed: the sale happened and the units left.</summary>
    Sale = 1,

    /// <summary>Units were held for a cart or an order. On hand does not move.</summary>
    Reservation = 2,

    /// <summary>A hold was given up, expired or cancelled. On hand does not move.</summary>
    Release = 3,

    /// <summary>Units came back from a customer and passed QC.</summary>
    Return = 4,

    /// <summary>An operator changed the count deliberately, with a reason.</summary>
    Adjustment = 5,

    /// <summary>Units were written off as unsellable.</summary>
    Damage = 6,

    /// <summary>The receiving half of a transfer between two locations.</summary>
    TransferIn = 7,

    /// <summary>The sending half of a transfer between two locations.</summary>
    TransferOut = 8,

    /// <summary>A stock take found the count wrong and this is the difference.</summary>
    Correction = 9,
}

/// <summary>
/// One immutable movement of stock (docs/02-domain-model.md §4.2).
/// </summary>
/// <remarks>
/// <para>
/// The record of truth. Both quantity columns on <see cref="StockItem"/> are caches of sums over
/// this table, which is what makes "why is there one fewer than yesterday" a question with an
/// answer rather than a theory.
/// </para>
/// <para>
/// Append-only and partitioned monthly on <see cref="OccurredAt"/>. Append-only because a ledger
/// that can be edited is not a ledger; partitioned because this is the highest-volume table in the
/// commerce spine — every sale, hold, release and expiry writes one — and a partitioned table is
/// created partitioned or not at all.
/// </para>
/// <para>
/// Two signed deltas, not one. <see cref="Change"/> moves on hand and <see cref="ReservedChange"/>
/// moves reserved, and a movement may touch either or both: a hold is <c>(0, +n)</c>, a sale is
/// <c>(-n, -n)</c>, a goods receipt is <c>(+n, 0)</c>. Without the second column, "reserved equals
/// the ledger sum" is not something the reconciliation job could assert, and a lost hold would be
/// invisible.
/// </para>
/// </remarks>
internal sealed class StockLedgerEntry : Entity<Guid>, ITenantScoped, IAppendOnly, IPartitioned
{
    private StockLedgerEntry(
        Guid id,
        Guid stockItemId,
        int change,
        int balanceAfter,
        int reservedChange,
        int reservedAfter,
        StockMovementReason reason,
        DateTimeOffset occurredAt)
        : base(id)
    {
        StockItemId = Guard.NotEmpty(stockItemId);
        Change = change;
        BalanceAfter = balanceAfter;
        ReservedChange = reservedChange;
        ReservedAfter = reservedAfter;
        Reason = reason;
        OccurredAt = occurredAt;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private StockLedgerEntry()
    {
    }

    /// <summary>The stock row that moved.</summary>
    public Guid StockItemId { get; private set; }

    /// <summary>Signed change to units on hand. Zero for a pure hold or release.</summary>
    public int Change { get; private set; }

    /// <summary>Units on hand after this entry. The value the conditional update returned.</summary>
    public int BalanceAfter { get; private set; }

    /// <summary>Signed change to units reserved. Zero for a movement that does not touch holds.</summary>
    public int ReservedChange { get; private set; }

    /// <summary>Units reserved after this entry.</summary>
    public int ReservedAfter { get; private set; }

    /// <summary>Why it moved.</summary>
    public StockMovementReason Reason { get; private set; }

    /// <summary>What caused it: <c>order</c>, <c>cart</c>, <c>purchase_order</c>, <c>stock_take</c>.</summary>
    public string? ReferenceType { get; private set; }

    /// <summary>The id of whatever caused it, in the module that owns that thing.</summary>
    public Guid? ReferenceId { get; private set; }

    /// <summary>What the operator wrote, for a movement a person decided on.</summary>
    public string? Note { get; private set; }

    /// <summary>Who did it, or null for a movement the system made on its own.</summary>
    public Guid? ActorId { get; private set; }

    /// <summary>When it happened. The partition key.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>The longest note this platform stores against a movement.</summary>
    public const int MaxNoteLength = 500;

    /// <summary>Records a movement that has already been applied to the row.</summary>
    /// <param name="stockItemId">The stock row that moved.</param>
    /// <param name="change">Signed change to units on hand.</param>
    /// <param name="balanceAfter">Units on hand after.</param>
    /// <param name="reservedChange">Signed change to units reserved.</param>
    /// <param name="reservedAfter">Units reserved after.</param>
    /// <param name="reason">Why it moved.</param>
    /// <param name="occurredAt">When.</param>
    /// <param name="referenceType">What caused it.</param>
    /// <param name="referenceId">The id of whatever caused it.</param>
    /// <param name="note">What the operator wrote.</param>
    /// <param name="actorId">Who did it.</param>
    public static StockLedgerEntry Record(
        Guid stockItemId,
        int change,
        int balanceAfter,
        int reservedChange,
        int reservedAfter,
        StockMovementReason reason,
        DateTimeOffset occurredAt,
        string? referenceType = null,
        Guid? referenceId = null,
        string? note = null,
        Guid? actorId = null)
        => new(
            UuidV7.New(),
            stockItemId,
            change,
            balanceAfter,
            reservedChange,
            reservedAfter,
            reason,
            occurredAt)
        {
            ReferenceType = string.IsNullOrWhiteSpace(referenceType) ? null : referenceType.Trim(),
            ReferenceId = referenceId,
            Note = string.IsNullOrWhiteSpace(note)
                ? null
                : note.Trim()[..Math.Min(note.Trim().Length, MaxNoteLength)],
            ActorId = actorId,
        };
}
