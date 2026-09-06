using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Payments.Domain;

/// <summary>
/// One collection against one order (docs/03-database-design.md §4.9).
/// </summary>
/// <remarks>
/// <para>
/// The aggregate root of this module, and a deliberately narrow one. It knows an order id, an
/// amount, a currency and what the gateway has said about it — and nothing about what was bought,
/// from whom, or where it is going. Widening it would make the payments schema a second copy of the
/// sale, and a second copy is a second answer.
/// </para>
/// <para>
/// <see cref="Amount"/> is copied from the order's amount payable at creation and is never
/// recomputed. Everything else about the money — what was actually taken, what has gone back — is
/// written only from what the gateway reports, verified by an API re-fetch
/// (docs/07-security-compliance.md §4). The platform never confirms an order on a figure it made up
/// or on a figure a webhook body claimed.
/// </para>
/// <para>
/// Every mutator is idempotent, because every fact reaching them arrives at least once and possibly
/// out of order: a webhook is redelivered, a reconciliation sweep independently discovers the same
/// capture, and an operator presses <em>sync</em>. Applying the same capture twice must leave one
/// captured payment.
/// </para>
/// </remarks>
internal sealed class Payment : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private readonly List<PaymentAttempt> _attempts = [];
    private readonly List<Refund> _refunds = [];

    private Payment(
        Guid id,
        Guid orderId,
        string orderNumber,
        Guid customerId,
        string provider,
        decimal amount,
        string currencyCode,
        string idempotencyKey,
        DateTimeOffset createdAt)
        : base(id)
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        CustomerId = customerId;
        Provider = provider;
        Amount = amount;
        CurrencyCode = currencyCode;
        IdempotencyKey = idempotencyKey;
        Receipt = orderNumber;
        Status = PaymentStatus.Created;
        Method = PaymentMethod.Unknown;
        OpenedAt = createdAt;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Payment()
    {
        OrderNumber = string.Empty;
        Provider = string.Empty;
        CurrencyCode = Money.Inr;
        IdempotencyKey = string.Empty;
        Receipt = string.Empty;
    }

    /// <summary>The order this money is being collected for.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Its human-readable number. Sent to the gateway as the receipt.</summary>
    public string OrderNumber { get; private set; }

    /// <summary>The shopper.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>Which provider is collecting: <c>razorpay</c>, or <c>internal_cod</c> for cash.</summary>
    public string Provider { get; private set; }

    /// <summary>The rail it was actually taken on, as the gateway reports it.</summary>
    public PaymentMethod Method { get; private set; }

    /// <summary>The gateway's handle on the collection — the Razorpay order id. Null for cash.</summary>
    public string? ProviderOrderId { get; private set; }

    /// <summary>The gateway's id for the payment that succeeded, once one has.</summary>
    public string? ProviderPaymentId { get; private set; }

    /// <summary>What the gateway is being asked for. Copied from the order and never recomputed.</summary>
    public decimal Amount { get; private set; }

    /// <summary>What has actually been taken, from the gateway's own API.</summary>
    public decimal AmountCaptured { get; private set; }

    /// <summary>What has gone back. A cache of the refunds, reconciled against them nightly.</summary>
    public decimal AmountRefunded { get; private set; }

    /// <summary>ISO 4217 code every amount here is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>Where the collection stands.</summary>
    public PaymentStatus Status { get; private set; }

    /// <summary>
    /// The placement key this collection was opened under.
    /// </summary>
    /// <remarks>
    /// Unique per tenant, and that index is the whole of idempotency here: a replayed placement finds
    /// the collection it already opened instead of opening a second one against one order.
    /// </remarks>
    public string IdempotencyKey { get; private set; }

    /// <summary>What appears on the gateway's dashboard against this collection.</summary>
    public string Receipt { get; private set; }

    /// <summary>Free-form notes sent to and returned by the gateway. Never personal data.</summary>
    public string? Notes { get; private set; }

    /// <summary>When the collection was opened.</summary>
    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>When the shopper's bank held the money.</summary>
    public DateTimeOffset? AuthorizedAt { get; private set; }

    /// <summary>When the money was taken.</summary>
    public DateTimeOffset? CapturedAt { get; private set; }

    /// <summary>When it last failed.</summary>
    public DateTimeOffset? FailedAt { get; private set; }

    /// <summary>When the gateway stops accepting payment against it, when it says so.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>The gateway's own failure code, kept verbatim so it can be looked up.</summary>
    public string? FailureCode { get; private set; }

    /// <summary>What the shopper can be told about the failure.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>When reconciliation last agreed this payment with the gateway.</summary>
    public DateTimeOffset? ReconciledAt { get; private set; }

    /// <summary>The settlement report this payment was paid out in, once it has been.</summary>
    public Guid? SettlementId { get; private set; }

    /// <summary>Every try, including the ones that failed.</summary>
    public IReadOnlyList<PaymentAttempt> Attempts => _attempts;

    /// <summary>Every refund raised against it.</summary>
    public IReadOnlyList<Refund> Refunds => _refunds;

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

    /// <summary>What is still collectable: what was asked for, less what has already been taken.</summary>
    public decimal AmountOutstanding => Math.Max(0m, Amount - AmountCaptured);

    /// <summary>What could still be refunded out of this collection.</summary>
    public decimal AmountRefundable => Math.Max(0m, AmountCaptured - AmountRefunded);

    /// <summary>Opens a collection against an order.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="orderNumber">Its number, which becomes the gateway receipt.</param>
    /// <param name="customerId">The shopper.</param>
    /// <param name="provider">The provider collecting.</param>
    /// <param name="amount">What to collect. The order's amount payable, copied.</param>
    /// <param name="currencyCode">ISO 4217 code the amount is in.</param>
    /// <param name="idempotencyKey">The placement key, unique per tenant.</param>
    /// <param name="createdAt">The current instant.</param>
    public static Payment Open(
        Guid orderId,
        string orderNumber,
        Guid customerId,
        string provider,
        decimal amount,
        string currencyCode,
        string idempotencyKey,
        DateTimeOffset createdAt)
        => new(
            UuidV7.New(),
            orderId,
            Guard.NotNullOrWhiteSpace(orderNumber),
            customerId,
            Guard.NotNullOrWhiteSpace(provider),
            amount,
            Guard.NotNullOrWhiteSpace(currencyCode),
            Guard.NotNullOrWhiteSpace(idempotencyKey),
            createdAt);

    /// <summary>Records the gateway's handle on the collection, once it has been opened there.</summary>
    /// <param name="providerOrderId">The gateway order id.</param>
    /// <param name="expiresAt">When the gateway stops accepting payment, when it says.</param>
    public void AttachProviderOrder(string providerOrderId, DateTimeOffset? expiresAt = null)
    {
        ProviderOrderId = Guard.NotNullOrWhiteSpace(providerOrderId);
        ExpiresAt = expiresAt;
    }

    /// <summary>Appends a try. Never replaces one — the failed tries are the support record.</summary>
    /// <param name="attempt">What was tried, and what came back.</param>
    public void Record(PaymentAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        _attempts.Add(attempt);
    }

    /// <summary>Attaches a refund raised against this collection.</summary>
    /// <param name="refund">The refund.</param>
    public void Add(Refund refund)
    {
        ArgumentNullException.ThrowIfNull(refund);
        _refunds.Add(refund);
    }

    /// <summary>Records that the shopper's bank has held the money.</summary>
    /// <param name="providerPaymentId">The gateway's payment id.</param>
    /// <param name="method">The rail, as the gateway reports it.</param>
    /// <param name="at">When it was authorised.</param>
    /// <returns>Whether anything changed.</returns>
    public bool Authorize(string? providerPaymentId, PaymentMethod method, DateTimeOffset at)
    {
        // A late authorisation for a payment that has already been captured is not a regression; it
        // is an out-of-order webhook. The reference and the rail are still worth learning from it.
        ApplyProviderFacts(providerPaymentId, method);

        if (!PaymentLifecycle.IsAllowed(Status, PaymentStatus.Authorized)
            || Status != PaymentStatus.Created && Status != PaymentStatus.Failed)
        {
            return false;
        }

        Status = PaymentStatus.Authorized;
        AuthorizedAt ??= at;
        ClearFailure();

        return true;
    }

    /// <summary>
    /// Records that the money was taken.
    /// </summary>
    /// <remarks>
    /// <paramref name="amountCaptured"/> must be the figure re-fetched from the gateway's API, never
    /// one read out of a webhook body (docs/07-security-compliance.md §4). This method records what
    /// it is given; verifying it against the order is the caller's job, and is done before the call.
    /// </remarks>
    /// <param name="providerPaymentId">The gateway's payment id.</param>
    /// <param name="method">The rail, as the gateway reports it.</param>
    /// <param name="amountCaptured">What the gateway says it took.</param>
    /// <param name="at">When it was captured.</param>
    /// <returns>Whether this call is what moved the payment to captured.</returns>
    public bool Capture(string? providerPaymentId, PaymentMethod method, decimal amountCaptured, DateTimeOffset at)
    {
        ApplyProviderFacts(providerPaymentId, method);

        // Take the larger figure rather than the latest: two events reporting the same capture must
        // not add up, and a later event reporting less than we already hold would lose money.
        AmountCaptured = Math.Max(AmountCaptured, amountCaptured);

        if (PaymentLifecycle.IsSettled(Status))
        {
            return false;
        }

        if (!PaymentLifecycle.IsAllowed(Status, PaymentStatus.Captured))
        {
            return false;
        }

        Status = PaymentStatus.Captured;
        AuthorizedAt ??= at;
        CapturedAt ??= at;
        ClearFailure();

        return true;
    }

    /// <summary>Records that the gateway refused, or the shopper never finished.</summary>
    /// <param name="failureCode">The gateway's own code.</param>
    /// <param name="reason">What the shopper can be told.</param>
    /// <param name="at">When it failed.</param>
    /// <returns>Whether this call is what moved the payment to failed.</returns>
    public bool Fail(string? failureCode, string? reason, DateTimeOffset at)
    {
        // A failure arriving after money was taken is a stale event about an earlier attempt, and
        // acting on it would un-pay a paid order. The attempt row still records that it happened.
        if (PaymentLifecycle.IsSettled(Status) || Status == PaymentStatus.Cancelled)
        {
            return false;
        }

        FailureCode = Truncate(failureCode, 64);
        FailureReason = Truncate(reason, 500);
        FailedAt = at;

        if (Status == PaymentStatus.Failed)
        {
            return false;
        }

        Status = PaymentStatus.Failed;
        return true;
    }

    /// <summary>Abandons a collection nobody is going to complete.</summary>
    /// <param name="reason">Why, for the record.</param>
    /// <param name="at">When.</param>
    /// <returns>Whether anything changed.</returns>
    public bool Cancel(string? reason, DateTimeOffset at)
    {
        if (!PaymentLifecycle.IsAllowed(Status, PaymentStatus.Cancelled) || Status == PaymentStatus.Cancelled)
        {
            return false;
        }

        Status = PaymentStatus.Cancelled;
        FailureReason = Truncate(reason, 500);
        FailedAt ??= at;

        return true;
    }

    /// <summary>Sends the shopper back to the widget on the same collection.</summary>
    /// <returns>Whether the collection is now open again.</returns>
    public bool Reopen()
    {
        if (Status != PaymentStatus.Failed)
        {
            return Status == PaymentStatus.Created;
        }

        Status = PaymentStatus.Created;
        ClearFailure();

        return true;
    }

    /// <summary>
    /// Re-derives the refunded total and the status from the refunds that actually completed.
    /// </summary>
    /// <remarks>
    /// Summed rather than incremented, deliberately. An increment applied twice by a redelivered
    /// webhook would refund the shopper's money twice on paper; a sum of the terminal-success rows
    /// gives the same answer however many times it is called.
    /// </remarks>
    public void RederiveRefunds()
    {
        AmountRefunded = _refunds
            .Where(refund => refund.Status == RefundStatus.Processed)
            .Sum(refund => refund.Amount);

        Status = PaymentLifecycle.AfterRefund(AmountCaptured, AmountRefunded, Status);
    }

    /// <summary>Stamps the moment reconciliation last agreed this payment with the gateway.</summary>
    /// <param name="at">The current instant.</param>
    public void MarkReconciled(DateTimeOffset at) => ReconciledAt = at;

    /// <summary>Records which settlement report paid this collection out.</summary>
    /// <param name="settlementId">The settlement.</param>
    public void MarkSettled(Guid settlementId) => SettlementId = settlementId;

    /// <summary>Replaces the notes sent alongside the collection.</summary>
    /// <param name="notes">The notes, or null to clear them.</param>
    public void SetNotes(string? notes)
        => Notes = string.IsNullOrWhiteSpace(notes) ? null : Truncate(notes.Trim(), 2000);

    /// <summary>
    /// Learns the gateway's identifiers without moving the payment.
    /// </summary>
    /// <remarks>
    /// The reference is written once. A second, different payment id against one collection means
    /// the shopper paid twice — keeping the first makes the duplicate visible in the attempts rather
    /// than overwriting the evidence of it.
    /// </remarks>
    private void ApplyProviderFacts(string? providerPaymentId, PaymentMethod method)
    {
        if (!string.IsNullOrWhiteSpace(providerPaymentId))
        {
            ProviderPaymentId ??= providerPaymentId;
        }

        if (method != PaymentMethod.Unknown)
        {
            Method = method;
        }
    }

    private void ClearFailure()
    {
        FailureCode = null;
        FailureReason = null;
    }

    private static string? Truncate(string? value, int max)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= max ? value : value[..max];
}
