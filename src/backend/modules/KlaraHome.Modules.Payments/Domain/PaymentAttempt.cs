using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Payments.Domain;

/// <summary>
/// What a gateway told us about one attempt, in a shape it is safe to keep.
/// </summary>
/// <remarks>
/// <para>
/// The masking rule lives here rather than in the adapter, so there is one place that decides what
/// this platform is allowed to remember about a payment instrument. Nothing on this record can
/// identify an instrument: a card is a network and its last four digits, a UPI payment is the
/// handle without the identifier before it, and a PAN, a CVV or a full VPA has no property to be
/// written to (docs/07-security-compliance.md §4).
/// </para>
/// <para>
/// It is <c>jsonb</c> rather than columns because every rail describes itself differently and the
/// set grows whenever the gateway adds one — but it is a closed type rather than a free-form
/// document precisely so that a future adapter cannot quietly start storing a number.
/// </para>
/// </remarks>
internal sealed class PaymentMethodDetail
{
    /// <summary>Card network, bank name, wallet name or UPI provider, as the gateway names it.</summary>
    public string? Issuer { get; set; }

    /// <summary>The last four digits of a card. Never more, and never anything else.</summary>
    public string? Last4 { get; set; }

    /// <summary>The domain half of a UPI handle — <c>okhdfcbank</c> — never the identifier before it.</summary>
    public string? UpiHandle { get; set; }

    /// <summary>Card type as the gateway reports it: <c>credit</c>, <c>debit</c>, <c>prepaid</c>.</summary>
    public string? CardType { get; set; }

    /// <summary>Whether the gateway reported this as an international instrument.</summary>
    public bool? International { get; set; }

    /// <summary>The number of instalments, for an EMI payment.</summary>
    public int? EmiMonths { get; set; }
}

/// <summary>
/// One try at collecting, and what came back (docs/03-database-design.md §4.9).
/// </summary>
/// <remarks>
/// <para>
/// Append-only, and that is the point: "the customer says they were charged three times" is the
/// commonest payment support call there is, and a single mutable status column on the payment
/// cannot answer it. Every fact that arrives about a collection — from the browser, from a webhook,
/// from a reconciliation sweep, from an operator — becomes a row here whether or not it moved the
/// payment.
/// </para>
/// <para>
/// <see cref="Source"/> is what makes a lost webhook visible. An attempt whose source is
/// <see cref="PaymentAttemptSource.Reconciliation"/> is one the gateway never delivered to us, and a
/// run of them is a webhook endpoint that has stopped working.
/// </para>
/// </remarks>
internal sealed class PaymentAttempt : Entity<Guid>, ITenantScoped, IAppendOnly
{
    private PaymentAttempt(Guid id, Guid paymentId, PaymentAttemptSource source, DateTimeOffset attemptedAt)
        : base(id)
    {
        PaymentId = paymentId;
        Source = source;
        AttemptedAt = attemptedAt;
        Detail = new PaymentMethodDetail();
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private PaymentAttempt() => Detail = new PaymentMethodDetail();

    /// <summary>The collection this try belongs to.</summary>
    public Guid PaymentId { get; private set; }

    /// <summary>The gateway's id for this try, where it gave one.</summary>
    public string? ProviderPaymentId { get; private set; }

    /// <summary>What the gateway said the try's state was, in its own words.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>The rail it was tried on.</summary>
    public PaymentMethod Method { get; private set; }

    /// <summary>What is safe to remember about the instrument. Never identifying.</summary>
    public PaymentMethodDetail Detail { get; private set; }

    /// <summary>What the try was for.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The gateway's error code, where it refused.</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>The gateway's description of the refusal.</summary>
    public string? ErrorDescription { get; private set; }

    /// <summary>Which route learned about this try.</summary>
    public PaymentAttemptSource Source { get; private set; }

    /// <summary>When it happened.</summary>
    public DateTimeOffset AttemptedAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Records a try.</summary>
    /// <param name="paymentId">The collection.</param>
    /// <param name="providerPaymentId">The gateway's id for it, when there is one.</param>
    /// <param name="status">What the gateway called the state.</param>
    /// <param name="method">The rail.</param>
    /// <param name="amount">What was being collected.</param>
    /// <param name="detail">What is safe to remember about the instrument.</param>
    /// <param name="errorCode">The gateway's refusal code.</param>
    /// <param name="errorDescription">Its description of the refusal.</param>
    /// <param name="source">Which route learned about it.</param>
    /// <param name="attemptedAt">When it happened.</param>
    public static PaymentAttempt Record(
        Guid paymentId,
        string? providerPaymentId,
        string status,
        PaymentMethod method,
        decimal amount,
        PaymentMethodDetail? detail,
        string? errorCode,
        string? errorDescription,
        PaymentAttemptSource source,
        DateTimeOffset attemptedAt)
        => new(UuidV7.New(), paymentId, source, attemptedAt)
        {
            ProviderPaymentId = string.IsNullOrWhiteSpace(providerPaymentId) ? null : providerPaymentId,
            Status = string.IsNullOrWhiteSpace(status) ? "unknown" : status,
            Method = method,
            Amount = amount,
            Detail = detail ?? new PaymentMethodDetail(),
            ErrorCode = Clip(errorCode, 64),
            ErrorDescription = Clip(errorDescription, 500),
        };

    private static string? Clip(string? value, int max)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= max ? value : value[..max];
}
