using KlaraHome.Modules.Payments.Domain;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Payments.Infrastructure.Gateway;

/// <summary>The provider names this platform knows, as they are stored.</summary>
/// <remarks>
/// Strings rather than an enum because they are persisted on every payment row and read by an
/// operator. A future provider is a new adapter and a new constant, never a schema change.
/// </remarks>
internal static class PaymentProviders
{
    /// <summary>The gateway. Hosted checkout only — no card data reaches these servers (ADR-008).</summary>
    public const string Razorpay = "razorpay";

    /// <summary>Cash at the door. A provider with no gateway behind it, on purpose.</summary>
    public const string InternalCod = "internal_cod";
}

/// <summary>What to open a collection for.</summary>
/// <param name="PaymentId">Our own id for the collection, sent as a note so the gateway echoes it back.</param>
/// <param name="Receipt">The order number, which is what appears on the gateway's dashboard.</param>
/// <param name="Amount">What to collect, in the store currency.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="Notes">Non-personal key/values echoed back on every event about this collection.</param>
internal sealed record PaymentIntentRequest(
    Guid PaymentId,
    string Receipt,
    decimal Amount,
    string CurrencyCode,
    IReadOnlyDictionary<string, string> Notes);

/// <summary>What the storefront needs to open the checkout widget.</summary>
/// <param name="ProviderOrderId">The gateway's handle on the collection.</param>
/// <param name="PublicKey">The publishable key the widget is initialised with. Never the secret.</param>
/// <param name="Amount">What the gateway was asked for.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
/// <param name="ExpiresAt">When the gateway stops accepting payment against it, where it says.</param>
internal sealed record PaymentIntent(
    string ProviderOrderId,
    string PublicKey,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// A payment as the gateway currently describes it.
/// </summary>
/// <remarks>
/// The shape every route into this module reduces to: a webhook is parsed into one of these, a
/// re-fetch returns one, and a reconciliation sweep asks for one. Having a single shape is what lets
/// the workflow that applies a payment fact be written once rather than three times, once per route.
/// </remarks>
/// <param name="ProviderPaymentId">The gateway's id for the payment.</param>
/// <param name="Status">Its own status word, kept verbatim for the attempt record.</param>
/// <param name="Method">The rail, mapped onto this platform's vocabulary.</param>
/// <param name="Amount">What the gateway says the payment is for.</param>
/// <param name="AmountRefunded">What the gateway says has gone back out of it.</param>
/// <param name="IsAuthorized">Whether the money is held.</param>
/// <param name="IsCaptured">Whether the money has been taken. The fact that confirms an order.</param>
/// <param name="IsFailed">Whether the gateway refused it.</param>
/// <param name="Detail">What is safe to remember about the instrument.</param>
/// <param name="ErrorCode">The gateway's refusal code.</param>
/// <param name="ErrorDescription">Its description of the refusal.</param>
/// <param name="OccurredAt">When the gateway says it happened.</param>
internal sealed record ProviderPayment(
    string ProviderPaymentId,
    string Status,
    PaymentMethod Method,
    decimal Amount,
    decimal AmountRefunded,
    bool IsAuthorized,
    bool IsCaptured,
    bool IsFailed,
    PaymentMethodDetail Detail,
    string? ErrorCode,
    string? ErrorDescription,
    DateTimeOffset? OccurredAt);

/// <summary>A refund as the gateway currently describes it.</summary>
/// <param name="ProviderRefundId">The gateway's id for the refund.</param>
/// <param name="ProviderPaymentId">The payment it came out of.</param>
/// <param name="Status">Its own status word.</param>
/// <param name="Amount">What went back.</param>
/// <param name="IsProcessed">Whether the money has actually moved.</param>
/// <param name="IsFailed">Whether the gateway refused it.</param>
/// <param name="Error">Why, when it refused.</param>
/// <param name="OccurredAt">When the gateway says it happened.</param>
internal sealed record ProviderRefund(
    string ProviderRefundId,
    string? ProviderPaymentId,
    string Status,
    decimal Amount,
    bool IsProcessed,
    bool IsFailed,
    string? Error,
    DateTimeOffset? OccurredAt);

/// <summary>One line of a settlement report, as the gateway reports it.</summary>
/// <param name="EntryType">What the movement is: <c>payment</c>, <c>refund</c>, <c>adjustment</c>, <c>transfer</c>.</param>
/// <param name="ProviderEntryId">The gateway's id for the line.</param>
/// <param name="ProviderPaymentId">The payment or refund it concerns.</param>
/// <param name="Amount">The gross amount.</param>
/// <param name="Fee">The gateway's fee.</param>
/// <param name="Tax">The GST on that fee.</param>
/// <param name="Debit">What left the merchant balance.</param>
/// <param name="Credit">What entered it.</param>
/// <param name="OccurredAt">When it happened.</param>
internal sealed record ProviderSettlementEntry(
    string EntryType,
    string? ProviderEntryId,
    string? ProviderPaymentId,
    decimal Amount,
    decimal Fee,
    decimal Tax,
    decimal Debit,
    decimal Credit,
    DateTimeOffset? OccurredAt);

/// <summary>A settlement report, as the gateway paid it.</summary>
/// <param name="ProviderSettlementId">The gateway's id for the settlement.</param>
/// <param name="Amount">What reached the bank, net of fees and tax.</param>
/// <param name="Fees">What the gateway kept.</param>
/// <param name="Tax">The GST on those fees.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="Utr">The bank reference the money arrived under.</param>
/// <param name="Status">The gateway's status word.</param>
/// <param name="SettledAt">When it was settled.</param>
/// <param name="Raw">The report as returned, kept whole.</param>
/// <param name="Entries">Its lines.</param>
internal sealed record ProviderSettlement(
    string ProviderSettlementId,
    decimal Amount,
    decimal Fees,
    decimal Tax,
    string CurrencyCode,
    string? Utr,
    string? Status,
    DateTimeOffset? SettledAt,
    string Raw,
    IReadOnlyList<ProviderSettlementEntry> Entries);

/// <summary>What a webhook turned out to be about, once its body was parsed.</summary>
/// <param name="ProviderEventId">The gateway's id for the event. The replay-protection key.</param>
/// <param name="EventType">What happened, in the gateway's vocabulary.</param>
/// <param name="OccurredAt">When the gateway says it happened.</param>
/// <param name="ProviderOrderId">The collection it concerns, where the payload names one.</param>
/// <param name="ProviderPaymentId">The payment it concerns, where the payload names one.</param>
/// <param name="ProviderRefundId">The refund it concerns, where the payload names one.</param>
/// <param name="PaymentId">Our own collection id, echoed back in the notes we sent.</param>
/// <param name="ProviderTransferId">
/// The seller payout it concerns, on a Route transfer event. This module owns no payouts and does
/// nothing with it beyond handing it to the module that does (Step 28B, deliverable 22).
/// </param>
internal sealed record WebhookEnvelope(
    string ProviderEventId,
    string EventType,
    DateTimeOffset? OccurredAt,
    string? ProviderOrderId,
    string? ProviderPaymentId,
    string? ProviderRefundId,
    Guid? PaymentId,
    string? ProviderTransferId = null);

/// <summary>
/// The gateway, behind an interface this platform owns (docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// No vendor type crosses this line. That is what makes the product redistributable: a client who
/// uses a different gateway writes one adapter, and nothing in the domain, the endpoints or the jobs
/// changes. It is also why the interface speaks in this platform's vocabulary — <see cref="PaymentMethod"/>,
/// not <c>"netbanking"</c> — and why parsing a webhook body is the adapter's job rather than a
/// handler's.
/// </para>
/// <para>
/// Everything returns a <see cref="Result{T}"/> rather than throwing. A gateway being unreachable is
/// an ordinary outcome on this path, not an exception: the order stays awaiting payment, the shopper
/// gets a retry link, and the reconciliation job picks it up. A failure here must never be able to
/// take down the request that discovered it.
/// </para>
/// <para>
/// <see cref="IsConfigured"/> is what lets this module ship before its credentials exist. An
/// unconfigured provider is registered, is honest about being unusable, and refuses with a named
/// error rather than throwing a null reference at a shopper.
/// </para>
/// </remarks>
internal interface IPaymentProvider
{
    /// <summary>Which provider this is, as it is stored on a payment.</summary>
    string Name { get; }

    /// <summary>Whether this deployment has the credentials to actually use it.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// The publishable key a browser initialises the checkout widget with, or empty for a provider
    /// that has no widget.
    /// </summary>
    /// <remarks>
    /// Read from the provider on every request rather than stored on the payment. It is a deployment
    /// credential, and a rotated key must not leave orders opened last week pointing at the key they
    /// were opened under. It is also the <em>only</em> credential that ever leaves this process.
    /// </remarks>
    string PublicKey { get; }

    /// <summary>Opens a collection at the gateway.</summary>
    /// <param name="request">What to collect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<PaymentIntent>> CreatePaymentIntentAsync(
        PaymentIntentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Re-reads a payment from the gateway. The only figure this platform will act on.</summary>
    /// <param name="providerPaymentId">The gateway's payment id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<ProviderPayment>> FetchPaymentAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the payments made against one collection, newest first.</summary>
    /// <remarks>
    /// The recovery path when a webhook was lost: this platform knows the gateway order id from
    /// placement, and this is how it discovers a payment id it was never told about.
    /// </remarks>
    /// <param name="providerOrderId">The gateway's order id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<IReadOnlyList<ProviderPayment>>> FetchPaymentsForOrderAsync(
        string providerOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>Takes money the gateway is holding.</summary>
    /// <param name="providerPaymentId">The gateway's payment id.</param>
    /// <param name="amount">How much to take.</param>
    /// <param name="currencyCode">ISO 4217 code the amount is in.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<ProviderPayment>> CapturePaymentAsync(
        string providerPaymentId,
        decimal amount,
        string currencyCode,
        CancellationToken cancellationToken = default);

    /// <summary>Sends money back.</summary>
    /// <param name="providerPaymentId">The payment to refund out of.</param>
    /// <param name="amount">How much.</param>
    /// <param name="currencyCode">ISO 4217 code the amount is in.</param>
    /// <param name="idempotencyKey">The refund's key, so a retried send does not refund twice.</param>
    /// <param name="speed">How quickly to send it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<ProviderRefund>> RefundAsync(
        string providerPaymentId,
        decimal amount,
        string currencyCode,
        string idempotencyKey,
        RefundSpeed speed,
        CancellationToken cancellationToken = default);

    /// <summary>Re-reads a refund from the gateway.</summary>
    /// <param name="providerRefundId">The gateway's refund id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<ProviderRefund>> FetchRefundAsync(
        string providerRefundId,
        CancellationToken cancellationToken = default);

    /// <summary>Pulls the settlement reports for a window.</summary>
    /// <param name="from">Start of the window, inclusive.</param>
    /// <param name="to">End of the window, inclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<IReadOnlyList<ProviderSettlement>>> FetchSettlementsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a webhook against the raw body.
    /// </summary>
    /// <remarks>
    /// The <em>raw</em> body, before any deserialisation: a re-serialised document is a different
    /// byte sequence and would never verify. Constant-time comparison, so a wrong signature takes
    /// the same time to reject as a nearly-right one.
    /// </remarks>
    /// <param name="rawBody">The bytes as they arrived.</param>
    /// <param name="signature">The signature header.</param>
    bool VerifyWebhookSignature(string rawBody, string? signature);

    /// <summary>
    /// Verifies the handshake the browser hands back when the checkout widget closes.
    /// </summary>
    /// <remarks>
    /// A UX signal and never a confirmation. It proves the browser talked to the gateway; it does
    /// not prove money moved, and this platform confirms an order on the webhook plus an API
    /// re-fetch and on nothing else (docs/07-security-compliance.md §4).
    /// </remarks>
    /// <param name="providerOrderId">The gateway order id the widget was opened with.</param>
    /// <param name="providerPaymentId">The payment id it handed back.</param>
    /// <param name="signature">The signature it handed back.</param>
    bool VerifyCheckoutSignature(string providerOrderId, string providerPaymentId, string? signature);

    /// <summary>Reads what a webhook is about, without acting on it.</summary>
    /// <param name="rawBody">The bytes as they arrived.</param>
    Result<WebhookEnvelope> ReadWebhook(string rawBody);
}
