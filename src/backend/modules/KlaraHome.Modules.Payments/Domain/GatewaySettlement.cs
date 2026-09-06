using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Payments.Domain;

/// <summary>
/// One settlement report, as the gateway paid it out (docs/03-database-design.md §4.9).
/// </summary>
/// <remarks>
/// <para>
/// The gateway does not pay out per payment; it pays a batch into the merchant's bank on a cycle,
/// net of its fees and the tax on them. This is that batch. Matching it against what this platform
/// recorded is the third of the three reconciliation questions in docs/08-integrations.md §1, and
/// the only one that can catch a payment that was captured, never disputed, and never actually paid.
/// </para>
/// <para>
/// The counts are stored rather than derived on read because they are the answer to "is this
/// report clean", asked on a list screen across months of reports — a per-row aggregate over the
/// entries would make that list a table scan of every payment the platform has ever taken.
/// </para>
/// </remarks>
internal sealed class GatewaySettlement : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private readonly List<GatewaySettlementEntry> _entries = [];

    private GatewaySettlement(
        Guid id,
        string provider,
        string providerSettlementId,
        decimal amount,
        string currencyCode,
        DateTimeOffset importedAt)
        : base(id)
    {
        Provider = provider;
        ProviderSettlementId = providerSettlementId;
        Amount = amount;
        CurrencyCode = currencyCode;
        ImportedAt = importedAt;
        Status = "processed";
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private GatewaySettlement()
    {
        Provider = string.Empty;
        ProviderSettlementId = string.Empty;
        CurrencyCode = Money.Inr;
        Status = string.Empty;
    }

    /// <summary>Which gateway paid it.</summary>
    public string Provider { get; private set; }

    /// <summary>The gateway's id for the settlement. Unique per tenant — imports are idempotent.</summary>
    public string ProviderSettlementId { get; private set; }

    /// <summary>What reached the bank, net of fees and tax.</summary>
    public decimal Amount { get; private set; }

    /// <summary>What the gateway kept in fees.</summary>
    public decimal Fees { get; private set; }

    /// <summary>The GST on those fees, which is input credit and belongs on a return.</summary>
    public decimal Tax { get; private set; }

    /// <summary>ISO 4217 code every amount here is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>The bank reference the money arrived under. What finance matches against a statement.</summary>
    public string? Utr { get; private set; }

    /// <summary>The gateway's own status word for the settlement.</summary>
    public string Status { get; private set; }

    /// <summary>When the gateway settled it.</summary>
    public DateTimeOffset? SettledAt { get; private set; }

    /// <summary>When this platform imported it.</summary>
    public DateTimeOffset ImportedAt { get; private set; }

    /// <summary>How many lines it has.</summary>
    public int EntryCount { get; private set; }

    /// <summary>How many of them matched a payment of ours, at the right amount.</summary>
    public int MatchedCount { get; private set; }

    /// <summary>How many did not. Non-zero is what an operator has to look at.</summary>
    public int MismatchCount { get; private set; }

    /// <summary>The report as the gateway returned it, kept whole.</summary>
    public string Raw { get; private set; } = "{}";

    /// <summary>Its lines.</summary>
    public IReadOnlyList<GatewaySettlementEntry> Entries => _entries;

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

    /// <summary>Imports a settlement report.</summary>
    /// <param name="provider">Which gateway paid it.</param>
    /// <param name="providerSettlementId">Its id for the settlement.</param>
    /// <param name="amount">What reached the bank.</param>
    /// <param name="fees">What the gateway kept.</param>
    /// <param name="tax">The GST on those fees.</param>
    /// <param name="currencyCode">ISO 4217 code the amounts are in.</param>
    /// <param name="utr">The bank reference.</param>
    /// <param name="status">The gateway's status word.</param>
    /// <param name="settledAt">When the gateway settled it.</param>
    /// <param name="raw">The report as returned.</param>
    /// <param name="importedAt">The current instant.</param>
    public static GatewaySettlement Import(
        string provider,
        string providerSettlementId,
        decimal amount,
        decimal fees,
        decimal tax,
        string currencyCode,
        string? utr,
        string? status,
        DateTimeOffset? settledAt,
        string raw,
        DateTimeOffset importedAt)
        => new(
            UuidV7.NewAt(importedAt),
            Guard.NotNullOrWhiteSpace(provider),
            Guard.NotNullOrWhiteSpace(providerSettlementId),
            amount,
            Guard.NotNullOrWhiteSpace(currencyCode),
            importedAt)
        {
            Fees = fees,
            Tax = tax,
            Utr = string.IsNullOrWhiteSpace(utr) ? null : utr,
            Status = string.IsNullOrWhiteSpace(status) ? "processed" : status,
            SettledAt = settledAt,
            Raw = string.IsNullOrWhiteSpace(raw) ? "{}" : raw,
        };

    /// <summary>Attaches one line of the report.</summary>
    /// <param name="entry">The line.</param>
    public void Add(GatewaySettlementEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Add(entry);
    }

    /// <summary>Recomputes the reconciliation counts from the lines actually attached.</summary>
    /// <remarks>
    /// Derived from the entries rather than incremented as they are matched, so a re-run of
    /// reconciliation over the same report reaches the same counts instead of doubling them.
    /// </remarks>
    public void RederiveCounts()
    {
        EntryCount = _entries.Count;
        MatchedCount = _entries.Count(entry => entry.MatchStatus == SettlementMatchStatus.Matched);
        MismatchCount = _entries.Count(entry => entry.MatchStatus != SettlementMatchStatus.Matched);
    }
}

/// <summary>
/// One line of a settlement report (docs/03-database-design.md §4.9).
/// </summary>
/// <remarks>
/// A row rather than an element of a <c>jsonb</c> array, because the entries are what is actually
/// queried: "which of our captures has the gateway not settled" and "which settled amount does not
/// equal what we recorded" are both per-entry questions, and a mismatch has to be addressable by
/// the human who is going to act on one line of it.
/// </remarks>
internal sealed class GatewaySettlementEntry : Entity<Guid>, ITenantScoped
{
    private GatewaySettlementEntry(Guid id, Guid settlementId, string entryType, decimal amount)
        : base(id)
    {
        SettlementId = settlementId;
        EntryType = entryType;
        Amount = amount;
        MatchStatus = SettlementMatchStatus.Unmatched;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private GatewaySettlementEntry() => EntryType = string.Empty;

    /// <summary>The report this line belongs to.</summary>
    public Guid SettlementId { get; private set; }

    /// <summary>What kind of movement it is: <c>payment</c>, <c>refund</c>, <c>adjustment</c>, <c>transfer</c>.</summary>
    public string EntryType { get; private set; }

    /// <summary>The gateway's id for the line itself.</summary>
    public string? ProviderEntryId { get; private set; }

    /// <summary>The gateway's payment or refund id this line concerns.</summary>
    public string? ProviderPaymentId { get; private set; }

    /// <summary>The collection it was matched to, once matching resolved one.</summary>
    public Guid? PaymentId { get; private set; }

    /// <summary>The gross amount of the movement.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The gateway's fee on it.</summary>
    public decimal Fee { get; private set; }

    /// <summary>The GST on that fee.</summary>
    public decimal Tax { get; private set; }

    /// <summary>What left the merchant balance on this line.</summary>
    public decimal Debit { get; private set; }

    /// <summary>What entered it.</summary>
    public decimal Credit { get; private set; }

    /// <summary>Whether it matched what this platform recorded.</summary>
    public SettlementMatchStatus MatchStatus { get; private set; }

    /// <summary>What did not agree, in a sentence somebody can act on.</summary>
    public string? MismatchReason { get; private set; }

    /// <summary>When the movement happened, as the gateway reports it.</summary>
    public DateTimeOffset? OccurredAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Records one line of a report.</summary>
    /// <param name="settlementId">The report.</param>
    /// <param name="entryType">What kind of movement it is.</param>
    /// <param name="providerEntryId">The gateway's id for the line.</param>
    /// <param name="providerPaymentId">The payment or refund it concerns.</param>
    /// <param name="amount">The gross amount.</param>
    /// <param name="fee">The gateway's fee.</param>
    /// <param name="tax">The GST on the fee.</param>
    /// <param name="debit">What left the balance.</param>
    /// <param name="credit">What entered it.</param>
    /// <param name="occurredAt">When it happened.</param>
    public static GatewaySettlementEntry Record(
        Guid settlementId,
        string entryType,
        string? providerEntryId,
        string? providerPaymentId,
        decimal amount,
        decimal fee,
        decimal tax,
        decimal debit,
        decimal credit,
        DateTimeOffset? occurredAt)
        => new(UuidV7.New(), settlementId, Guard.NotNullOrWhiteSpace(entryType), amount)
        {
            ProviderEntryId = string.IsNullOrWhiteSpace(providerEntryId) ? null : providerEntryId,
            ProviderPaymentId = string.IsNullOrWhiteSpace(providerPaymentId) ? null : providerPaymentId,
            Fee = fee,
            Tax = tax,
            Debit = debit,
            Credit = credit,
            OccurredAt = occurredAt,
        };

    /// <summary>Records that this line agrees with a collection of ours.</summary>
    /// <param name="paymentId">The collection.</param>
    public void Match(Guid paymentId)
    {
        PaymentId = paymentId;
        MatchStatus = SettlementMatchStatus.Matched;
        MismatchReason = null;
    }

    /// <summary>Records that it does not, and why.</summary>
    /// <param name="paymentId">The collection it was about, when one was found at all.</param>
    /// <param name="reason">What did not agree.</param>
    public void Mismatch(Guid? paymentId, string reason)
    {
        PaymentId = paymentId;
        MatchStatus = SettlementMatchStatus.Mismatched;
        MismatchReason = string.IsNullOrWhiteSpace(reason)
            ? "The settled line does not agree with what was recorded."
            : reason.Length <= 500 ? reason : reason[..500];
    }
}
