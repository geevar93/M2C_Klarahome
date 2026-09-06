using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Settlements.Domain;

/// <summary>Which way an amount moves the seller's balance.</summary>
/// <remarks>
/// Every entry carries a positive <see cref="LedgerEntry.Amount"/> and one of these. Signed amounts
/// were the alternative and are worse: a report that forgets a minus sign reads as a credit, and
/// "sum the credits, sum the debits, subtract" is a sentence an auditor can check.
/// </remarks>
internal enum LedgerDirection
{
    /// <summary>Increases what the platform owes the seller.</summary>
    Credit = 0,

    /// <summary>Decreases it.</summary>
    Debit = 1,
}

/// <summary>
/// What a ledger entry is (docs/03-database-design.md §4.12).
/// </summary>
/// <remarks>
/// <para>
/// Strings rather than an enum because they are read by an operator on a statement and grouped on by
/// a report, and because a new kind of charge is a new constant and a new report row — never a
/// migration. The order below is the order a statement reads in: what was sold, what the platform
/// took, what came back, what the state took, and what was paid.
/// </para>
/// <para>
/// Every type is either always a credit or always a debit, except <see cref="Adjustment"/>, which is
/// the one entry a human writes and therefore the one that can go either way.
/// </para>
/// </remarks>
internal static class LedgerEntryTypes
{
    /// <summary>The seller's supply, inclusive of tax. Credit.</summary>
    public const string Sale = "sale";

    /// <summary>What the platform charged for the sale, exclusive of tax on it. Debit.</summary>
    public const string Commission = "commission";

    /// <summary>The GST on everything the platform charged. Debit.</summary>
    /// <remarks>
    /// One entry for the tax on the commission, the marketplace fee and the gateway fee together,
    /// because the platform raises one tax invoice for all three and a seller claims input credit
    /// against that invoice. It is separate from the charges themselves because it is a different
    /// line on a different return — the charges are the platform's income and this is its output
    /// tax — and a single combined figure would leave the seller unable to claim it.
    /// </remarks>
    public const string PlatformTax = "platform_tax";

    /// <summary>The flat marketplace fee, with its own tax. Debit.</summary>
    public const string PlatformFee = "platform_fee";

    /// <summary>The payment gateway's cut, with its own tax, where the seller bears it. Debit.</summary>
    public const string PaymentFee = "payment_fee";

    /// <summary>The freight the platform paid, where the seller bears it. Debit.</summary>
    public const string ShippingFee = "shipping_fee";

    /// <summary>A supply reversed by a cancellation or a credit note, inclusive of tax. Debit.</summary>
    public const string Refund = "refund";

    /// <summary>The commission and fees given back with that reversal. Credit.</summary>
    public const string RefundCommissionReversal = "refund_commission_reversal";

    /// <summary>Tax collected at source under section 52 of the CGST Act. Debit.</summary>
    public const string Tcs = "tcs";

    /// <summary>Tax deducted at source under section 194-O of the Income-tax Act. Debit.</summary>
    public const string Tds = "tds";

    /// <summary>A correction somebody made deliberately. Either direction.</summary>
    public const string Adjustment = "adjustment";

    /// <summary>Money actually sent to the seller. Debit.</summary>
    public const string Payout = "payout";

    /// <summary>Every type this module posts, in the order a statement reads them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Sale,
        Commission,
        PlatformTax,
        PlatformFee,
        PaymentFee,
        ShippingFee,
        Refund,
        RefundCommissionReversal,
        Tcs,
        Tds,
        Adjustment,
        Payout,
    ];

    /// <summary>The types that make up what the platform charged for a sale.</summary>
    /// <remarks>
    /// Grouped here rather than at each call site, so the "total fees" on a cycle, on a statement and
    /// in the revenue report are the same list of things.
    /// </remarks>
    public static readonly IReadOnlyList<string> Charges =
    [
        Commission,
        PlatformTax,
        PlatformFee,
        PaymentFee,
        ShippingFee,
    ];

    /// <summary>Whether a type is one this platform knows.</summary>
    /// <param name="type">The candidate.</param>
    public static bool Contains(string? type)
        => type is not null && All.Contains(type, StringComparer.Ordinal);

    /// <summary>
    /// Where a type sits in the order a statement reads, for sorting a summary.
    /// </summary>
    /// <remarks>
    /// A method rather than a call to <c>IndexOf</c>, because <see cref="All"/> is exposed as a
    /// read-only list and one that grows is one whose ordering would otherwise be restated at every
    /// call site. An unknown type sorts last rather than first, so a type added tomorrow appears at
    /// the bottom of an old report instead of at the top of it.
    /// </remarks>
    /// <param name="type">The type.</param>
    public static int Rank(string? type)
    {
        for (var index = 0; index < All.Count; index++)
        {
            if (string.Equals(All[index], type, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return All.Count;
    }
}

/// <summary>What a ledger entry points back at.</summary>
/// <remarks>
/// An entry always names its source document — that is the second invariant in
/// docs/02-domain-model.md §4.6, and it is what turns "why am I being charged this" from an
/// argument into a lookup.
/// </remarks>
internal static class LedgerReferenceTypes
{
    /// <summary>One seller's part of an order. The source of nearly every entry.</summary>
    public const string SubOrder = "sub-order";

    /// <summary>A credit note raised on a return.</summary>
    public const string CreditNote = "credit-note";

    /// <summary>A settlement cycle, for the deductions computed on the period as a whole.</summary>
    public const string Cycle = "cycle";

    /// <summary>A payout item, for the money that discharged the balance.</summary>
    public const string Payout = "payout";

    /// <summary>A human, for an adjustment nothing else caused.</summary>
    public const string Manual = "manual";

    /// <summary>Every reference type this module writes.</summary>
    public static readonly IReadOnlyList<string> All = [SubOrder, CreditNote, Cycle, Payout, Manual];
}

/// <summary>
/// One movement on a seller's account (docs/03-database-design.md §4.12).
/// </summary>
/// <remarks>
/// <para>
/// Append-only, and that is the whole design. A seller's balance is not a column anywhere: it is
/// <c>Σ credits − Σ debits</c> over these rows, so it cannot drift from its own history, and a
/// correction is a reversing entry rather than an edit. The day somebody needs to know why a figure
/// changed, the rows that changed it are still there.
/// </para>
/// <para>
/// <see cref="SourceKey"/> is what makes posting idempotent. Delivery of an integration event is
/// at-least-once, so a redelivered "this parcel arrived" would otherwise credit a seller twice; the
/// key is derived from the fact rather than generated, and a unique index on it turns the second
/// attempt into a collision instead of a second payment.
/// </para>
/// <para>
/// <see cref="TaxableValue"/> is carried beside the amount because the two statutory deductions read
/// from different bases: TCS under section 52 is charged on the net value of taxable supplies, and
/// TDS under section 194-O on the gross amount. Recomputing either from the other at settlement
/// would need a GST rate that is no longer the one that applied.
/// </para>
/// </remarks>
internal sealed class LedgerEntry : Entity<Guid>, ITenantScoped, IVendorScoped, IAppendOnly, IAuditable
{
    private LedgerEntry(
        Guid id,
        Guid vendorId,
        string entryType,
        LedgerDirection direction,
        decimal amount,
        string currencyCode,
        string referenceType,
        Guid? referenceId,
        string sourceKey,
        DateTimeOffset occurredAt)
        : base(id)
    {
        VendorId = vendorId;
        EntryType = entryType;
        Direction = direction;
        Amount = amount;
        CurrencyCode = currencyCode;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        SourceKey = sourceKey;
        OccurredAt = occurredAt;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private LedgerEntry()
    {
        EntryType = string.Empty;
        CurrencyCode = Money.Inr;
        ReferenceType = string.Empty;
        SourceKey = string.Empty;
    }

    /// <inheritdoc />
    /// <remarks>Never null on this table: a movement with no seller is a movement with no account.</remarks>
    public Guid? VendorId { get; private set; }

    /// <summary>What kind of movement it is: one of <see cref="LedgerEntryTypes"/>.</summary>
    public string EntryType { get; private set; }

    /// <summary>Which way it moves the balance.</summary>
    public LedgerDirection Direction { get; private set; }

    /// <summary>How much, always positive.</summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// The value the tax was computed on, where the movement had one.
    /// </summary>
    /// <remarks>
    /// Zero for an entry that is not a supply — a payout has no taxable value, and neither does a
    /// gateway fee charged on. It is the base TCS is computed from, so it is on the row rather than
    /// re-derived from the amount and a rate that may since have changed.
    /// </remarks>
    public decimal TaxableValue { get; private set; }

    /// <summary>ISO 4217 code the amount is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>What the entry points back at: one of <see cref="LedgerReferenceTypes"/>.</summary>
    public string ReferenceType { get; private set; }

    /// <summary>The id of that document.</summary>
    public Guid? ReferenceId { get; private set; }

    /// <summary>The seller's part of the order that caused it, where one did.</summary>
    public Guid? SubOrderId { get; private set; }

    /// <summary>The order line, for the entries that are per line rather than per parcel.</summary>
    public Guid? OrderLineId { get; private set; }

    /// <summary>The cycle this entry was drawn into, or null while it is still unsettled.</summary>
    public Guid? SettlementCycleId { get; private set; }

    /// <summary>The payout batch that discharged it, for a payout entry.</summary>
    public Guid? PayoutBatchId { get; private set; }

    /// <summary>
    /// The stable key that makes posting this entry idempotent.
    /// </summary>
    /// <remarks>
    /// Derived from the fact — the sub-order, the credit note, the cycle — and the entry type, never
    /// generated. It is unique per tenant, which is what a redelivered event collides on.
    /// </remarks>
    public string SourceKey { get; private set; }

    /// <summary>When the movement happened, which is what a period selects on.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>What it is, in words on a statement.</summary>
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

    /// <summary>The amount as it moves the balance: positive for a credit, negative for a debit.</summary>
    /// <remarks>
    /// Computed rather than stored, so the signed view and the stored pair can never disagree. Not
    /// mapped — a report that needs the sign in SQL writes the <c>CASE</c> itself.
    /// </remarks>
    public decimal SignedAmount => Direction == LedgerDirection.Credit ? Amount : -Amount;

    /// <summary>Whether the entry has been drawn into a cycle and is no longer free to be.</summary>
    public bool IsSettled => SettlementCycleId is not null;

    /// <summary>Posts a movement.</summary>
    /// <param name="vendorId">The seller whose account moves.</param>
    /// <param name="entryType">What kind of movement: one of <see cref="LedgerEntryTypes"/>.</param>
    /// <param name="direction">Which way.</param>
    /// <param name="amount">How much, positive.</param>
    /// <param name="currencyCode">The currency.</param>
    /// <param name="referenceType">What it points back at.</param>
    /// <param name="referenceId">The id of that document.</param>
    /// <param name="sourceKey">The key a redelivery collides on.</param>
    /// <param name="occurredAt">When the movement happened.</param>
    public static LedgerEntry Post(
        Guid vendorId,
        string entryType,
        LedgerDirection direction,
        decimal amount,
        string currencyCode,
        string referenceType,
        Guid? referenceId,
        string sourceKey,
        DateTimeOffset occurredAt)
        => new(
            UuidV7.New(),
            Guard.NotEmpty(vendorId),
            Guard.NotNullOrWhiteSpace(entryType),
            direction,
            Guard.NotNegative(amount),
            Guard.NotNullOrWhiteSpace(currencyCode),
            Guard.NotNullOrWhiteSpace(referenceType),
            referenceId,
            Guard.NotNullOrWhiteSpace(sourceKey),
            occurredAt);

    /// <summary>Names the sale the movement came from.</summary>
    /// <param name="subOrderId">The seller's part of the order.</param>
    /// <param name="orderLineId">The line, for a per-line charge.</param>
    public LedgerEntry Against(Guid? subOrderId, Guid? orderLineId = null)
    {
        SubOrderId = subOrderId;
        OrderLineId = orderLineId;
        return this;
    }

    /// <summary>Records what the tax on the movement was computed on.</summary>
    /// <param name="taxableValue">The taxable value, never negative.</param>
    public LedgerEntry Taxed(decimal taxableValue)
    {
        TaxableValue = Guard.NotNegative(taxableValue);
        return this;
    }

    /// <summary>Describes the movement in the words a statement shows.</summary>
    /// <param name="note">The description.</param>
    public LedgerEntry Describe(string? note)
    {
        Note = note;
        return this;
    }

    /// <summary>
    /// Draws the entry into a settlement cycle.
    /// </summary>
    /// <remarks>
    /// The one thing that ever changes on an entry, and it is not the money: an append-only row can
    /// still be filed, and an entry can be filed exactly once — which is the invariant that stops one
    /// earning being paid in two cycles.
    /// </remarks>
    /// <param name="cycleId">The cycle.</param>
    public void AssignTo(Guid cycleId)
    {
        if (SettlementCycleId is not null && SettlementCycleId != cycleId)
        {
            throw new InvalidOperationException(
                $"Ledger entry {Id} is already settled in cycle {SettlementCycleId}.");
        }

        SettlementCycleId = Guard.NotEmpty(cycleId);
    }

    /// <summary>Records the batch that actually sent the money, on a payout entry.</summary>
    /// <param name="payoutBatchId">The batch.</param>
    public void PaidIn(Guid payoutBatchId) => PayoutBatchId = Guard.NotEmpty(payoutBatchId);
}
