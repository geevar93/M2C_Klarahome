using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Settlements.Domain;

/// <summary>
/// A run of payments to sellers (docs/03-database-design.md §4.12).
/// </summary>
/// <remarks>
/// <para>
/// A batch exists so that money leaving the platform is one decision rather than a hundred. It is
/// built from closed settlement cycles, signed off by somebody who did not build it, and only then
/// sent — and the aggregate refuses the self-approval outright, so the control does not depend on
/// the endpoint remembering to check.
/// </para>
/// <para>
/// Its state is <em>derived</em> from its items once they stop moving. A batch is not partially
/// failed because an operator marked it so; it is partially failed because some of its transfers
/// were refused, which is a fact only the gateway supplies.
/// </para>
/// <para>
/// There is no retry on a failed item. Retrying inside the batch would leave one row with two
/// outcomes and no way to say which attempt a bank reference belonged to; instead the cycle becomes
/// payable again and a new batch is built, and the failed one stands as the record of what was tried.
/// </para>
/// </remarks>
internal sealed class PayoutBatch : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private readonly List<PayoutItem> _items = [];

    private PayoutBatch(Guid id, string reference, string currencyCode, DateTimeOffset createdAt, Guid? createdBy)
        : base(id)
    {
        Reference = reference;
        CurrencyCode = currencyCode;
        Status = PayoutBatchStatus.Draft;
        RequestedAt = createdAt;
        RequestedBy = createdBy;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private PayoutBatch()
    {
        Reference = string.Empty;
        CurrencyCode = Money.Inr;
    }

    /// <summary>The human-readable reference finance quotes, as <c>PAY-2609-000042</c>.</summary>
    public string Reference { get; private set; }

    /// <summary>Where the batch stands.</summary>
    public PayoutBatchStatus Status { get; private set; }

    /// <summary>What the batch is worth in total.</summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>How many sellers it pays.</summary>
    public int VendorCount { get; private set; }

    /// <summary>ISO 4217 code every amount is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>Who built it.</summary>
    public Guid? RequestedBy { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>Who signed it off. Never the same person as <see cref="RequestedBy"/>.</summary>
    public Guid? ApprovedBy { get; private set; }

    /// <summary>When it was signed off.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>When it was handed to the gateway.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>When every item had stopped moving.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>The gateway's id for the run, where the provider gives one.</summary>
    public string? ProviderBatchId { get; private set; }

    /// <summary>Which adapter sent it, so a batch is still traceable after the default changes.</summary>
    public string? Provider { get; private set; }

    /// <summary>Why it was abandoned, when it was.</summary>
    public string? CancelledReason { get; private set; }

    /// <summary>What is in it: one line per seller.</summary>
    public IReadOnlyList<PayoutItem> Items => _items;

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

    /// <summary>Whether nothing further can happen to it.</summary>
    public bool IsTerminal => PayoutLifecycle.IsTerminal(Status);

    /// <summary>What actually reached a seller, which is what the ledger is debited by.</summary>
    public decimal SettledAmount
        => _items.Where(item => item.Status == PayoutItemStatus.Completed).Sum(item => item.Amount);

    /// <summary>Opens a batch.</summary>
    /// <param name="reference">Its allocated reference.</param>
    /// <param name="currencyCode">The currency.</param>
    /// <param name="requestedAt">When.</param>
    /// <param name="requestedBy">Who, or null when the scheduler built it.</param>
    public static PayoutBatch Draft(
        string reference,
        string currencyCode,
        DateTimeOffset requestedAt,
        Guid? requestedBy)
        => new(
            UuidV7.New(),
            Guard.NotNullOrWhiteSpace(reference),
            Guard.NotNullOrWhiteSpace(currencyCode),
            requestedAt,
            requestedBy);

    /// <summary>Adds one seller's line to a draft batch.</summary>
    /// <param name="item">The line.</param>
    public void Add(PayoutItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Status != PayoutBatchStatus.Draft)
        {
            throw new InvalidOperationException($"Payout batch {Reference} is {Status} and cannot be added to.");
        }

        _items.Add(item);
        Retotal();
    }

    /// <summary>Recomputes the batch's headline figures from its items.</summary>
    /// <remarks>
    /// The totals are stored because a batch is a document somebody signed; they are derived because
    /// a stored figure that disagrees with its own lines is the one thing a payment run must not
    /// have. Recomputing on every change is how both stay true.
    /// </remarks>
    public void Retotal()
    {
        var payable = _items.Where(item => item.Status != PayoutItemStatus.Skipped).ToArray();

        TotalAmount = payable.Sum(item => item.Amount);
        VendorCount = payable.Select(item => item.VendorId).Distinct().Count();
    }

    /// <summary>
    /// Signs the batch off.
    /// </summary>
    /// <remarks>
    /// The self-approval check is here rather than only in the handler, and the database carries it a
    /// third time as a check constraint. Three places sounds excessive for one rule; it is the
    /// cheapest rule in the system to get wrong and the most expensive to have got wrong.
    /// </remarks>
    /// <param name="approverId">Who is signing.</param>
    /// <param name="approvedAt">When.</param>
    public void Approve(Guid approverId, DateTimeOffset approvedAt)
    {
        Guard.NotEmpty(approverId);

        if (!PayoutLifecycle.IsAllowed(Status, PayoutBatchStatus.Approved, PayoutActor.Checker))
        {
            throw new InvalidOperationException($"Payout batch {Reference} is {Status} and cannot be approved.");
        }

        if (RequestedBy is { } maker && maker == approverId)
        {
            throw new InvalidOperationException(
                $"Payout batch {Reference} cannot be approved by the person who raised it.");
        }

        Status = PayoutBatchStatus.Approved;
        ApprovedBy = approverId;
        ApprovedAt = approvedAt;
    }

    /// <summary>Marks the batch as handed to the gateway.</summary>
    /// <param name="provider">Which adapter is sending it.</param>
    /// <param name="processedAt">When.</param>
    public void BeginProcessing(string provider, DateTimeOffset processedAt)
    {
        if (!PayoutLifecycle.Exists(Status, PayoutBatchStatus.Processing))
        {
            throw new InvalidOperationException($"Payout batch {Reference} is {Status} and cannot be sent.");
        }

        Status = PayoutBatchStatus.Processing;
        Provider = Guard.NotNullOrWhiteSpace(provider);
        ProcessedAt = processedAt;
    }

    /// <summary>Records the gateway's own id for the run, where it gives one.</summary>
    /// <param name="providerBatchId">The gateway's id.</param>
    public void RecordProviderBatch(string? providerBatchId) => ProviderBatchId = providerBatchId;

    /// <summary>
    /// Settles the batch's own state from its items, if they have all stopped moving.
    /// </summary>
    /// <remarks>
    /// Called after every gateway answer, and it does nothing while anything is still in flight. A
    /// batch that reported itself complete with a transfer still pending would mark a cycle paid
    /// that had not been.
    /// </remarks>
    /// <param name="completedAt">When the last item settled.</param>
    /// <returns>Whether the batch reached a terminal state on this call.</returns>
    public bool TrySettle(DateTimeOffset completedAt)
    {
        if (Status != PayoutBatchStatus.Processing)
        {
            return false;
        }

        if (_items.Exists(item => item.Status is PayoutItemStatus.Pending or PayoutItemStatus.Processing))
        {
            return false;
        }

        var completed = _items.Count(item => item.Status == PayoutItemStatus.Completed);
        var failed = _items.Count - completed;

        Status = PayoutLifecycle.Outcome(completed, failed);
        CompletedAt = completedAt;

        return true;
    }

    /// <summary>Abandons a batch nothing has left.</summary>
    /// <param name="reason">Why.</param>
    public void Cancel(string? reason)
    {
        if (!PayoutLifecycle.Exists(Status, PayoutBatchStatus.Cancelled))
        {
            throw new InvalidOperationException($"Payout batch {Reference} is {Status} and cannot be cancelled.");
        }

        Status = PayoutBatchStatus.Cancelled;
        CancelledReason = reason;
    }
}

/// <summary>
/// One seller's transfer within a batch (docs/03-database-design.md §4.12).
/// </summary>
/// <remarks>
/// <para>
/// One row per seller per batch, carrying the cycle it discharges and the destination it was sent
/// to. The destination is recorded at the moment of sending rather than read back from the seller's
/// record afterwards: a seller who changes their bank account next month must not change what a
/// payout made last month says it paid.
/// </para>
/// <para>
/// A skipped item is not a failure and is not silence. A seller with no verified account still gets
/// a row, with the reason on it, so the question "why was this seller not paid" has an answer in the
/// batch rather than in somebody's memory.
/// </para>
/// </remarks>
internal sealed class PayoutItem : Entity<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private PayoutItem(
        Guid id,
        Guid payoutBatchId,
        Guid vendorId,
        Guid? settlementCycleId,
        decimal amount,
        string currencyCode)
        : base(id)
    {
        PayoutBatchId = payoutBatchId;
        VendorId = vendorId;
        SettlementCycleId = settlementCycleId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = PayoutItemStatus.Pending;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private PayoutItem() => CurrencyCode = Money.Inr;

    /// <summary>The batch this line belongs to.</summary>
    public Guid PayoutBatchId { get; private set; }

    /// <inheritdoc />
    /// <remarks>Never null: a payout line exists for exactly one seller.</remarks>
    public Guid? VendorId { get; private set; }

    /// <summary>The seller's code, frozen for the statement.</summary>
    public string? VendorCode { get; private set; }

    /// <summary>The seller's legal name, frozen for the statement.</summary>
    public string? VendorName { get; private set; }

    /// <summary>The settlement cycle this transfer discharges.</summary>
    public Guid? SettlementCycleId { get; private set; }

    /// <summary>What is being sent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO 4217 code the amount is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>Where the line stands.</summary>
    public PayoutItemStatus Status { get; private set; }

    /// <summary>The gateway account the money was sent to, frozen at sending.</summary>
    public string? DestinationAccountId { get; private set; }

    /// <summary>The last four digits of the bank account, for the statement.</summary>
    public string? DestinationLast4 { get; private set; }

    /// <summary>The gateway's id for the transfer.</summary>
    public string? ProviderPayoutId { get; private set; }

    /// <summary>The gateway's own status word, kept verbatim.</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>The bank's unique transaction reference, once the gateway reports one.</summary>
    public string? Utr { get; private set; }

    /// <summary>Why it did not go, when it did not.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>When it was handed to the gateway.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>When it stopped moving, either way.</summary>
    public DateTimeOffset? SettledAt { get; private set; }

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

    /// <summary>Whether the gateway has given its final answer on this line.</summary>
    public bool IsSettled
        => Status is PayoutItemStatus.Completed or PayoutItemStatus.Failed or PayoutItemStatus.Skipped;

    /// <summary>Adds a seller to a batch.</summary>
    /// <param name="payoutBatchId">The batch.</param>
    /// <param name="vendorId">The seller.</param>
    /// <param name="settlementCycleId">The cycle being discharged.</param>
    /// <param name="amount">What to send.</param>
    /// <param name="currencyCode">The currency.</param>
    public static PayoutItem For(
        Guid payoutBatchId,
        Guid vendorId,
        Guid? settlementCycleId,
        decimal amount,
        string currencyCode)
        => new(
            UuidV7.New(),
            Guard.NotEmpty(payoutBatchId),
            Guard.NotEmpty(vendorId),
            settlementCycleId,
            Guard.NotNegative(amount),
            Guard.NotNullOrWhiteSpace(currencyCode));

    /// <summary>Freezes who is being paid, for the statement.</summary>
    /// <param name="code">Their short code.</param>
    /// <param name="name">Their legal name.</param>
    public void Payee(string? code, string? name)
    {
        VendorCode = code;
        VendorName = name;
    }

    /// <summary>Records that the transfer was handed to the gateway.</summary>
    /// <param name="destinationAccountId">The gateway account it was sent to.</param>
    /// <param name="destinationLast4">The last four digits of the bank account.</param>
    /// <param name="providerPayoutId">The gateway's id, where it answered with one.</param>
    /// <param name="providerStatus">Its own status word.</param>
    /// <param name="sentAt">When.</param>
    public void Sent(
        string? destinationAccountId,
        string? destinationLast4,
        string? providerPayoutId,
        string? providerStatus,
        DateTimeOffset sentAt)
    {
        Status = PayoutItemStatus.Processing;
        DestinationAccountId = destinationAccountId;
        DestinationLast4 = destinationLast4;
        ProviderPayoutId = providerPayoutId;
        ProviderStatus = providerStatus;
        SentAt = sentAt;
        FailureReason = null;
    }

    /// <summary>Records that the money reached the seller.</summary>
    /// <param name="utr">The bank's unique transaction reference.</param>
    /// <param name="providerStatus">The gateway's own status word.</param>
    /// <param name="settledAt">When.</param>
    public void Complete(string? utr, string? providerStatus, DateTimeOffset settledAt)
    {
        Status = PayoutItemStatus.Completed;
        Utr = utr;
        ProviderStatus = providerStatus ?? ProviderStatus;
        SettledAt = settledAt;
        FailureReason = null;
    }

    /// <summary>Records that it did not.</summary>
    /// <param name="reason">Why, in the gateway's words where it gave any.</param>
    /// <param name="providerStatus">Its own status word.</param>
    /// <param name="settledAt">When.</param>
    public void Fail(string? reason, string? providerStatus, DateTimeOffset settledAt)
    {
        Status = PayoutItemStatus.Failed;
        FailureReason = Truncate(reason);
        ProviderStatus = providerStatus ?? ProviderStatus;
        SettledAt = settledAt;
    }

    /// <summary>Records that it was never attempted, and why.</summary>
    /// <param name="reason">What is missing, in words an operator can act on.</param>
    /// <param name="at">When the batch noticed.</param>
    public void Skip(string? reason, DateTimeOffset at)
    {
        Status = PayoutItemStatus.Skipped;
        FailureReason = Truncate(reason);
        SettledAt = at;
    }

    /// <summary>Keeps a gateway's error text inside the column it is stored in.</summary>
    /// <remarks>
    /// A provider is entitled to return a paragraph, and a payout that failed to record why it
    /// failed because the message was long would be the worst of both outcomes.
    /// </remarks>
    private static string? Truncate(string? reason)
        => reason is { Length: > 500 } ? reason[..500] : reason;
}
