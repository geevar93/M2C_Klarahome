using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Pricing.Domain;

/// <summary>Which way store credit moved, and why the amount is always positive.</summary>
internal enum WalletTransactionType
{
    /// <summary>Credit added — a refund, a loyalty accrual, a goodwill gesture.</summary>
    Credit = 0,

    /// <summary>Credit spent against an order.</summary>
    Debit = 1,

    /// <summary>Credit that lapsed unspent.</summary>
    Expiry = 2,

    /// <summary>A debit given back because the order it paid for was cancelled.</summary>
    Reversal = 3,
}

/// <summary>
/// A customer's store credit (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Balance"/> is a derived cache of the transaction rows, maintained in the same
/// transaction as the row that changes it — the same arrangement as the stock quantities in §4.5,
/// and for the same reason: the balance is read on every cart render and summing a ledger for it
/// would be the most expensive query in the basket.
/// </para>
/// <para>
/// The wallet is the platform's own liability to a shopper, not a seller's. It never reaches
/// Settlements: credit spent on an order is money the platform has already collected, and the
/// seller is paid from the order's value regardless.
/// </para>
/// </remarks>
internal sealed class Wallet : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private Wallet(Guid id, Guid customerId, string currencyCode)
        : base(id)
    {
        CustomerId = customerId;
        Balance = Money.ZeroIn(currencyCode);
        IsActive = true;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Wallet()
    {
    }

    /// <summary>The shopper. A plain id: no foreign key crosses a schema.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>What they have. Never negative; the constraint is in the database too.</summary>
    public Money Balance { get; private set; }

    /// <summary>Whether the wallet may still be spent from.</summary>
    public bool IsActive { get; private set; }

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

    /// <summary>Opens a wallet for a shopper.</summary>
    /// <param name="customerId">The shopper.</param>
    /// <param name="currencyCode">ISO 4217 code the balance is held in.</param>
    public static Wallet Open(Guid customerId, string currencyCode)
        => new(UuidV7.New(), Guard.NotEmpty(customerId), currencyCode);

    /// <summary>Opens or closes the wallet to spending.</summary>
    /// <param name="isActive">Whether it may be spent from.</param>
    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>
    /// Applies a movement and returns the transaction that records it, or null when the balance was
    /// too short for the debit.
    /// </summary>
    /// <remarks>
    /// All or nothing on a debit. A partial one would leave the order underpaid by a figure nobody
    /// quoted, and the caller has no way to tell the shopper what happened.
    /// </remarks>
    /// <param name="type">Which way it moved.</param>
    /// <param name="amount">How much. Always positive; the type carries the sign.</param>
    /// <param name="reason">Why, from <c>StoreCreditReasons</c>.</param>
    /// <param name="referenceType">What caused it.</param>
    /// <param name="referenceId">The causing record, for idempotency.</param>
    /// <param name="occurredAt">When.</param>
    /// <param name="expiresAt">When the credit lapses, for a credit that does.</param>
    /// <param name="note">Free text an operator typed.</param>
    public WalletTransaction? Apply(
        WalletTransactionType type,
        decimal amount,
        string reason,
        string? referenceType,
        Guid? referenceId,
        DateTimeOffset occurredAt,
        DateTimeOffset? expiresAt = null,
        string? note = null)
    {
        if (amount <= 0m)
        {
            return null;
        }

        var money = new Money(amount, Balance.Currency);
        var isDebit = type is WalletTransactionType.Debit or WalletTransactionType.Expiry;

        if (isDebit && Balance < money)
        {
            return null;
        }

        Balance = isDebit ? Balance - money : Balance + money;

        return WalletTransaction.Create(
            Id,
            type,
            money,
            Balance,
            reason,
            referenceType,
            referenceId,
            occurredAt,
            expiresAt,
            note);
    }
}

/// <summary>
/// One movement of store credit (docs/03-database-design.md §4.6). Append-only.
/// </summary>
/// <remarks>
/// <see cref="BalanceAfter"/> is stored rather than recomputed so a statement reads the same today
/// as it did when it was issued, whatever has happened to the wallet since.
/// </remarks>
internal sealed class WalletTransaction : Entity<Guid>, ITenantScoped, IAuditable, IAppendOnly
{
    private WalletTransaction(
        Guid id,
        Guid walletId,
        WalletTransactionType type,
        Money amount,
        Money balanceAfter,
        string reason,
        string? referenceType,
        Guid? referenceId,
        DateTimeOffset occurredAt,
        DateTimeOffset? expiresAt,
        string? note)
        : base(id)
    {
        WalletId = walletId;
        Type = type;
        Amount = amount;
        BalanceAfter = balanceAfter;
        Reason = Guard.NotNullOrWhiteSpace(reason);
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        OccurredAt = occurredAt;
        ExpiresAt = expiresAt;
        Note = note;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private WalletTransaction() => Reason = string.Empty;

    /// <summary>The wallet it moved.</summary>
    public Guid WalletId { get; private set; }

    /// <summary>Which way.</summary>
    public WalletTransactionType Type { get; private set; }

    /// <summary>How much. Always positive.</summary>
    public Money Amount { get; private set; }

    /// <summary>What the wallet held afterwards.</summary>
    public Money BalanceAfter { get; private set; }

    /// <summary>Why, from <c>StoreCreditReasons</c>.</summary>
    public string Reason { get; private set; }

    /// <summary>What caused it — <c>order</c>, <c>return</c>, <c>staff</c>.</summary>
    public string? ReferenceType { get; private set; }

    /// <summary>The causing record. Unique with the type and reference type, which is the idempotency key.</summary>
    public Guid? ReferenceId { get; private set; }

    /// <summary>When it happened.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>When this credit lapses, or null for credit that does not.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Free text an operator typed, shown on the statement.</summary>
    public string? Note { get; private set; }

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

    /// <summary>Records a movement. Called by <see cref="Wallet.Apply"/> and nowhere else.</summary>
    /// <param name="walletId">The wallet.</param>
    /// <param name="type">Which way it moved.</param>
    /// <param name="amount">How much.</param>
    /// <param name="balanceAfter">What the wallet held afterwards.</param>
    /// <param name="reason">Why.</param>
    /// <param name="referenceType">What caused it.</param>
    /// <param name="referenceId">The causing record.</param>
    /// <param name="occurredAt">When.</param>
    /// <param name="expiresAt">When this credit lapses.</param>
    /// <param name="note">Free text.</param>
    public static WalletTransaction Create(
        Guid walletId,
        WalletTransactionType type,
        Money amount,
        Money balanceAfter,
        string reason,
        string? referenceType,
        Guid? referenceId,
        DateTimeOffset occurredAt,
        DateTimeOffset? expiresAt,
        string? note)
        => new(
            UuidV7.New(),
            walletId,
            type,
            amount,
            balanceAfter,
            reason,
            referenceType,
            referenceId,
            occurredAt,
            expiresAt,
            note);
}
