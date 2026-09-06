using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Payments.Domain;

/// <summary>
/// Money going back out of a collection (docs/03-database-design.md §4.9).
/// </summary>
/// <remarks>
/// <para>
/// A row per refund rather than a running total on the payment, because a refund is a decision
/// somebody made: it has a reason, an initiator, sometimes an approver, and a life of its own at the
/// gateway. The total on the payment is derived from these and is a cache, not the record.
/// </para>
/// <para>
/// The maker-checker control is expressed in the type rather than in a handler
/// (docs/07-security-compliance.md §4). A refund above the configured threshold is created
/// <see cref="RefundStatus.Requested"/> and cannot be sent until a <em>different</em> user approves
/// it; one at or below is approved on creation by its initiator. <see cref="RequiresApproval"/> is
/// stored rather than recomputed so that raising the threshold next quarter cannot retroactively
/// make a completed refund look unapproved.
/// </para>
/// <para>
/// <see cref="Status"/> reaching <see cref="RefundStatus.Processed"/> is the only state that means
/// money has actually moved. Everything before it is an intention, and telling a shopper their money
/// is back on an intention is how a platform ends up arguing with a bank statement.
/// </para>
/// </remarks>
internal sealed class Refund : Entity<Guid>, ITenantScoped, IAuditable
{
    private Refund(
        Guid id,
        Guid paymentId,
        Guid orderId,
        decimal amount,
        string currencyCode,
        string reason,
        string idempotencyKey,
        DateTimeOffset initiatedAt)
        : base(id)
    {
        PaymentId = paymentId;
        OrderId = orderId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Reason = reason;
        IdempotencyKey = idempotencyKey;
        InitiatedAt = initiatedAt;
        Status = RefundStatus.Requested;
        Speed = RefundSpeed.Normal;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Refund()
    {
        CurrencyCode = Money.Inr;
        Reason = string.Empty;
        IdempotencyKey = string.Empty;
    }

    /// <summary>The collection this comes out of.</summary>
    public Guid PaymentId { get; private set; }

    /// <summary>The order, denormalised so a refund can be found without loading its payment.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The seller's part it relates to, when it relates to one.</summary>
    public Guid? SubOrderId { get; private set; }

    /// <summary>The return that caused it, from Step 17. Null for a cancellation refund.</summary>
    public Guid? ReturnId { get; private set; }

    /// <summary>What is going back.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO 4217 code the amount is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>Why. Required, and shown on the audit trail rather than only in a log.</summary>
    public string Reason { get; private set; }

    /// <summary>Where the refund stands.</summary>
    public RefundStatus Status { get; private set; }

    /// <summary>The gateway's id for it, once it has one.</summary>
    public string? ProviderRefundId { get; private set; }

    /// <summary>How quickly it was asked to be sent.</summary>
    public RefundSpeed Speed { get; private set; }

    /// <summary>Whether a second signature was needed when it was raised.</summary>
    public bool RequiresApproval { get; private set; }

    /// <summary>Who raised it. Null when the platform raised it for a cancellation.</summary>
    public Guid? InitiatedBy { get; private set; }

    /// <summary>When it was raised.</summary>
    public DateTimeOffset InitiatedAt { get; private set; }

    /// <summary>Who signed it off. Never the same user as <see cref="InitiatedBy"/>.</summary>
    public Guid? ApprovedBy { get; private set; }

    /// <summary>When it was signed off.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>Why a second signature was withheld.</summary>
    public string? RejectedReason { get; private set; }

    /// <summary>When the gateway confirmed the money had gone.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Why the gateway refused it.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// The key this refund was raised under, unique per tenant.
    /// </summary>
    /// <remarks>
    /// A refund is the one operation in this platform where a duplicate is money out of the door
    /// twice, so the index is on the table rather than trusting a caller to send a key: an
    /// automatic refund derives its key from what caused it, and an operator's is the request's.
    /// </remarks>
    public string IdempotencyKey { get; private set; }

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

    /// <summary>Whether the gateway still has work to do on it.</summary>
    public bool IsOpen => Status is RefundStatus.Requested or RefundStatus.Approved or RefundStatus.Processing;

    /// <summary>Raises a refund, and decides on the spot whether it needs a second signature.</summary>
    /// <param name="paymentId">The collection it comes out of.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="amount">What is going back.</param>
    /// <param name="currencyCode">ISO 4217 code the amount is in.</param>
    /// <param name="reason">Why.</param>
    /// <param name="idempotencyKey">The key, unique per tenant.</param>
    /// <param name="initiatedBy">Who raised it, or null when the platform did.</param>
    /// <param name="approvalThreshold">At or below this, it is approved on creation. Zero requires approval for everything.</param>
    /// <param name="initiatedAt">The current instant.</param>
    public static Refund Raise(
        Guid paymentId,
        Guid orderId,
        decimal amount,
        string currencyCode,
        string reason,
        string idempotencyKey,
        Guid? initiatedBy,
        decimal approvalThreshold,
        DateTimeOffset initiatedAt)
    {
        var refund = new Refund(
            UuidV7.New(),
            paymentId,
            orderId,
            Guard.NotNegative(amount),
            Guard.NotNullOrWhiteSpace(currencyCode),
            Guard.NotNullOrWhiteSpace(reason),
            Guard.NotNullOrWhiteSpace(idempotencyKey),
            initiatedAt)
        {
            InitiatedBy = initiatedBy,
            RequiresApproval = amount > approvalThreshold,
        };

        if (!refund.RequiresApproval)
        {
            // Approved by the act of raising it. ApprovedBy stays null rather than repeating the
            // initiator, so "who was the second person" has one honest answer: nobody was needed.
            refund.Status = RefundStatus.Approved;
            refund.ApprovedAt = initiatedAt;
        }

        return refund;
    }

    /// <summary>Attaches what caused it, when a return or one seller's parcel did.</summary>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="returnId">The return.</param>
    public void AttachCause(Guid? subOrderId, Guid? returnId)
    {
        SubOrderId = subOrderId;
        ReturnId = returnId;
    }

    /// <summary>Asks the gateway to send it faster, where the rail supports it.</summary>
    /// <param name="speed">The speed.</param>
    public void SetSpeed(RefundSpeed speed) => Speed = speed;

    /// <summary>
    /// Records the second signature.
    /// </summary>
    /// <remarks>
    /// The caller checks that <paramref name="approver"/> is not the initiator; a check constraint
    /// on the table refuses it as well, because a control that exists in only one of those two
    /// places is a control that a future handler can forget about.
    /// </remarks>
    /// <param name="approver">Who signed it off.</param>
    /// <param name="at">When.</param>
    /// <returns>Whether anything changed.</returns>
    public bool Approve(Guid approver, DateTimeOffset at)
    {
        if (Status != RefundStatus.Requested)
        {
            return false;
        }

        Status = RefundStatus.Approved;
        ApprovedBy = approver;
        ApprovedAt = at;

        return true;
    }

    /// <summary>Withholds the second signature. Nothing is sent to the gateway.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>Whether anything changed.</returns>
    public bool Reject(string? reason)
    {
        if (Status != RefundStatus.Requested)
        {
            return false;
        }

        Status = RefundStatus.Rejected;
        RejectedReason = Clip(reason, 500);

        return true;
    }

    /// <summary>Records that the gateway has it and has not finished.</summary>
    /// <param name="providerRefundId">The gateway's id for it.</param>
    /// <returns>Whether anything changed.</returns>
    public bool MarkProcessing(string? providerRefundId)
    {
        if (!string.IsNullOrWhiteSpace(providerRefundId))
        {
            ProviderRefundId ??= providerRefundId;
        }

        if (Status != RefundStatus.Approved)
        {
            return false;
        }

        Status = RefundStatus.Processing;
        return true;
    }

    /// <summary>Records that the money has gone back. The only state that is money moved.</summary>
    /// <param name="providerRefundId">The gateway's id for it.</param>
    /// <param name="at">When the gateway processed it.</param>
    /// <returns>Whether this call is what completed it.</returns>
    public bool MarkProcessed(string? providerRefundId, DateTimeOffset at)
    {
        if (!string.IsNullOrWhiteSpace(providerRefundId))
        {
            ProviderRefundId ??= providerRefundId;
        }

        if (Status == RefundStatus.Processed)
        {
            return false;
        }

        Status = RefundStatus.Processed;
        CompletedAt ??= at;
        FailureReason = null;

        return true;
    }

    /// <summary>Records that the gateway refused it.</summary>
    /// <param name="reason">Why.</param>
    /// <returns>Whether anything changed.</returns>
    public bool MarkFailed(string? reason)
    {
        // A failure arriving after the gateway already confirmed the money went back is a stale
        // event. Acting on it would tell a shopper their refund had been reversed.
        if (Status is RefundStatus.Processed or RefundStatus.Rejected)
        {
            return false;
        }

        Status = RefundStatus.Failed;
        FailureReason = Clip(reason, 500);

        return true;
    }

    private static string? Clip(string? value, int max)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= max ? value : value[..max];
}
