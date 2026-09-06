using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Settlements.Domain;

/// <summary>Where a settlement period stands.</summary>
/// <remarks>
/// Three states and no machine, because there are only two edges and both are one-way. A period
/// accrues, a period is closed, and a closed period is discharged by a payout. Going backwards is
/// not a transition — it is a reversing entry in a later cycle, which is what an append-only ledger
/// makes the only honest answer.
/// </remarks>
internal enum SettlementCycleStatus
{
    /// <summary>Still accruing. Entries continue to be drawn into it.</summary>
    Open = 0,

    /// <summary>Totalled and fixed. Nothing further is drawn in, and it may be paid.</summary>
    Closed = 1,

    /// <summary>Discharged by a completed payout.</summary>
    Paid = 2,
}

/// <summary>
/// One seller's settlement period (docs/03-database-design.md §4.12).
/// </summary>
/// <remarks>
/// <para>
/// A cycle is a line drawn under a stretch of the ledger, not a second copy of it. Its totals are
/// derived from the entries assigned to it and written down at the moment of closing, which is the
/// one place in this module where a computed figure becomes a stored one — because a statement a
/// seller was sent must still read the same next year, whatever has since been posted.
/// </para>
/// <para>
/// The period is half-open: <see cref="PeriodStart"/> is included and <see cref="PeriodEnd"/> is not.
/// It is the only arrangement in which consecutive periods neither overlap nor leave a gap, and both
/// of those would be money in the wrong week.
/// </para>
/// <para>
/// <see cref="OpeningBalance"/> is what the previous cycle left unpaid — a balance below the payout
/// minimum, or one a failed transfer never discharged. Carrying it forward rather than reopening the
/// old cycle is what keeps a closed statement closed.
/// </para>
/// </remarks>
internal sealed class SettlementCycle : AggregateRoot<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private SettlementCycle(
        Guid id,
        Guid vendorId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        decimal openingBalance,
        string currencyCode)
        : base(id)
    {
        VendorId = vendorId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        OpeningBalance = openingBalance;
        CurrencyCode = currencyCode;
        Status = SettlementCycleStatus.Open;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private SettlementCycle() => CurrencyCode = Money.Inr;

    /// <inheritdoc />
    /// <remarks>Never null: a cycle exists for exactly one seller.</remarks>
    public Guid? VendorId { get; private set; }

    /// <summary>The first instant the period covers.</summary>
    public DateTimeOffset PeriodStart { get; private set; }

    /// <summary>The first instant it does not. Exclusive, so periods tile without overlap.</summary>
    public DateTimeOffset PeriodEnd { get; private set; }

    /// <summary>Where the period stands.</summary>
    public SettlementCycleStatus Status { get; private set; }

    /// <summary>What the previous period left unpaid.</summary>
    public decimal OpeningBalance { get; private set; }

    /// <summary>What the seller supplied in the period, inclusive of tax.</summary>
    public decimal GrossSales { get; private set; }

    /// <summary>What that supply was worth before tax — the base TCS is charged on.</summary>
    public decimal TaxableSales { get; private set; }

    /// <summary>What the platform charged in commission, including the GST on it.</summary>
    public decimal TotalCommission { get; private set; }

    /// <summary>Marketplace, gateway and freight charges together.</summary>
    public decimal TotalFees { get; private set; }

    /// <summary>What came off the period through cancellations and credit notes, inclusive of tax.</summary>
    public decimal TotalRefunds { get; private set; }

    /// <summary>The taxable value of what came off, which reduces the TCS base.</summary>
    public decimal TaxableRefunds { get; private set; }

    /// <summary>Corrections a human posted, net of direction.</summary>
    public decimal TotalAdjustments { get; private set; }

    /// <summary>Money already sent to the seller inside the period.</summary>
    /// <remarks>
    /// Usually zero — a payout normally discharges the cycle before it and lands in the next one's
    /// opening balance. It is a column of its own because the alternative is a statement whose
    /// movements do not add up to its own net figure, which is the first thing a seller checks.
    /// </remarks>
    public decimal TotalPayouts { get; private set; }

    /// <summary>Tax collected at source under section 52 of the CGST Act.</summary>
    public decimal Tcs { get; private set; }

    /// <summary>Tax deducted at source under section 194-O of the Income-tax Act.</summary>
    public decimal Tds { get; private set; }

    /// <summary>What the seller is owed for the period, after everything and the opening balance.</summary>
    public decimal NetPayable { get; private set; }

    /// <summary>ISO 4217 code every amount is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>How many ledger entries were drawn into it.</summary>
    public int EntryCount { get; private set; }

    /// <summary>When it was closed.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Who closed it, or null when the scheduler did.</summary>
    public Guid? ClosedBy { get; private set; }

    /// <summary>When money against it actually left.</summary>
    public DateTimeOffset? PaidAt { get; private set; }

    /// <summary>The batch that discharged it.</summary>
    public Guid? PayoutBatchId { get; private set; }

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

    /// <summary>Everything the platform and the state took, together.</summary>
    public decimal TotalDeductions => TotalCommission + TotalFees + TotalRefunds + Tcs + Tds;

    /// <summary>Whether the period is finished with and may be put into a batch.</summary>
    public bool IsClosed => Status is SettlementCycleStatus.Closed or SettlementCycleStatus.Paid;

    /// <summary>Opens a period for a seller.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="periodStart">The first instant covered.</param>
    /// <param name="periodEnd">The first instant not covered.</param>
    /// <param name="openingBalance">What the previous period left unpaid.</param>
    /// <param name="currencyCode">The currency.</param>
    public static SettlementCycle Open(
        Guid vendorId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        decimal openingBalance,
        string currencyCode)
    {
        if (periodEnd <= periodStart)
        {
            throw new ArgumentException("A settlement period must end after it starts.", nameof(periodEnd));
        }

        return new SettlementCycle(
            UuidV7.New(),
            Guard.NotEmpty(vendorId),
            periodStart,
            periodEnd,
            openingBalance,
            Guard.NotNullOrWhiteSpace(currencyCode));
    }

    /// <summary>
    /// Writes the period's totals down and fixes them.
    /// </summary>
    /// <remarks>
    /// The net payable is computed here rather than passed in, so a caller cannot state a figure that
    /// disagrees with the parts it is made of. It may be negative — a period in which a seller's
    /// returns exceeded their sales owes the platform money, and the honest thing to do is carry it
    /// into the next period as a negative opening balance rather than pretend it is zero.
    /// </remarks>
    /// <param name="totals">What the entries drawn into the period came to.</param>
    /// <param name="tcs">Tax collected at source.</param>
    /// <param name="tds">Tax deducted at source.</param>
    /// <param name="entryCount">How many entries were drawn in.</param>
    /// <param name="closedAt">When.</param>
    /// <param name="closedBy">Who, or null for the scheduler.</param>
    public void Close(
        SettlementTotals totals,
        decimal tcs,
        decimal tds,
        int entryCount,
        DateTimeOffset closedAt,
        Guid? closedBy)
    {
        ArgumentNullException.ThrowIfNull(totals);

        if (Status != SettlementCycleStatus.Open)
        {
            throw new InvalidOperationException($"Settlement cycle {Id} is already {Status}.");
        }

        GrossSales = totals.GrossSales;
        TaxableSales = totals.TaxableSales;
        TotalCommission = totals.Commission;
        TotalFees = totals.Fees;
        TotalRefunds = totals.Refunds;
        TaxableRefunds = totals.TaxableRefunds;
        TotalAdjustments = totals.Adjustments;
        TotalPayouts = totals.Payouts;
        Tcs = Guard.NotNegative(tcs);
        Tds = Guard.NotNegative(tds);
        EntryCount = entryCount;

        // From the movement rather than from the itemised totals. They are the same arithmetic when
        // everything is right, and the movement is the one that cannot silently omit an entry type
        // somebody adds later — which is exactly the bug that would pay a seller the wrong figure and
        // still balance on the screen.
        NetPayable = OpeningBalance + totals.NetMovement - Tcs - Tds;

        Status = SettlementCycleStatus.Closed;
        ClosedAt = closedAt;
        ClosedBy = closedBy;
    }

    /// <summary>Records that money against the period actually left.</summary>
    /// <param name="payoutBatchId">The batch that sent it.</param>
    /// <param name="paidAt">When.</param>
    public void MarkPaid(Guid payoutBatchId, DateTimeOffset paidAt)
    {
        if (Status != SettlementCycleStatus.Closed)
        {
            throw new InvalidOperationException(
                $"Settlement cycle {Id} is {Status} and cannot be marked paid.");
        }

        PayoutBatchId = Guard.NotEmpty(payoutBatchId);
        PaidAt = paidAt;
        Status = SettlementCycleStatus.Paid;
    }

    /// <summary>Names the batch a cycle was put into, before the money has moved.</summary>
    /// <remarks>
    /// Separate from <see cref="MarkPaid"/> because a batch that is drafted, approved and then fails
    /// must leave the cycle payable. Only a completed transfer marks it paid.
    /// </remarks>
    /// <param name="payoutBatchId">The batch, or null to release it.</param>
    public void Batch(Guid? payoutBatchId) => PayoutBatchId = payoutBatchId;
}

/// <summary>What the entries drawn into a period came to, before the statutory deductions.</summary>
/// <remarks>
/// A separate object rather than eight arguments, because the totals are computed together from one
/// pass over the entries and passing them apart invites two of them being swapped — which is a bug
/// that reads as a plausible number.
/// </remarks>
/// <param name="GrossSales">Supplies in the period, inclusive of tax.</param>
/// <param name="TaxableSales">What those supplies were worth before tax.</param>
/// <param name="Commission">Commission and the GST on it.</param>
/// <param name="Fees">Marketplace, gateway and freight charges.</param>
/// <param name="Refunds">Supplies reversed, inclusive of tax.</param>
/// <param name="TaxableRefunds">What those reversals were worth before tax.</param>
/// <param name="Adjustments">Corrections a human posted, net of direction.</param>
/// <param name="Payouts">Money already sent inside the period.</param>
/// <param name="NetMovement">
/// Every entry drawn into the period, credits less debits.
/// <para>
/// Carried alongside the itemised figures rather than derived from them, and it is the one the net
/// payable is computed from. The itemised totals are a breakdown for a human; this is the sum of the
/// rows, and the day somebody adds a thirteenth entry type the breakdown would quietly omit it and
/// this would not.
/// </para>
/// </param>
internal sealed record SettlementTotals(
    decimal GrossSales,
    decimal TaxableSales,
    decimal Commission,
    decimal Fees,
    decimal Refunds,
    decimal TaxableRefunds,
    decimal Adjustments,
    decimal Payouts,
    decimal NetMovement)
{
    /// <summary>A period in which nothing happened.</summary>
    public static readonly SettlementTotals Empty = new(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m);

    /// <summary>The net value of taxable supplies — the base TCS under section 52 is charged on.</summary>
    /// <remarks>
    /// Net, because section 52(1) says so in as many words: the aggregate value of taxable supplies
    /// <em>reduced by</em> supplies returned. A negative net collects nothing rather than refunding
    /// tax the platform has already remitted — that correction belongs in the GSTR-8 amendment, not
    /// in a payout.
    /// </remarks>
    public decimal NetTaxableSupplies => Math.Max(0m, TaxableSales - TaxableRefunds);

    /// <summary>The gross amount of sales — the base TDS under section 194-O is deducted from.</summary>
    /// <remarks>
    /// Gross, and inclusive of GST, which is what distinguishes it from
    /// <see cref="NetTaxableSupplies"/>. The two bases sit side by side on a statement and are
    /// routinely confused; keeping both as named properties is cheaper than explaining the difference
    /// at every call site.
    /// </remarks>
    public decimal NetGrossSales => Math.Max(0m, GrossSales - Refunds);
}
