using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Modules.Payments.Infrastructure;

/// <summary>
/// The platform's own limits on collecting money.
/// </summary>
/// <remarks>
/// Configuration rather than store settings, on the same split every module here draws: sweeper
/// cadences, retry budgets and skew windows live where a shopkeeper cannot change them. The one
/// commercial lever that belongs to the operator — the value above which a refund needs a second
/// signature — is in the <c>payments</c> settings section instead, because it is a governance
/// decision that changes more often than the product is deployed.
/// </remarks>
internal sealed class PaymentsOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Payments";

    /// <summary>
    /// Which provider prepaid collections are opened with.
    /// </summary>
    /// <remarks>
    /// Named rather than inferred from whichever adapter happens to be configured. A deployment that
    /// has credentials for two gateways must still collect through exactly one, and discovering
    /// which by registration order would make that answer depend on a DI detail.
    /// </remarks>
    [Required]
    [StringLength(32, MinimumLength = 1)]
    public string Provider { get; set; } = "razorpay";

    /// <summary>
    /// Whether the gateway is asked to capture automatically rather than only authorise.
    /// </summary>
    /// <remarks>
    /// On. A marketplace that authorises and captures later has to reconcile a window in which the
    /// shopper's bank is holding money nobody has taken, and every hour of that window is an hour in
    /// which the hold can lapse on an order already promised to a seller.
    /// </remarks>
    public bool AutoCapture { get; set; } = true;

    /// <summary>
    /// How long after placement a shopper may still retry a failed payment, in hours.
    /// </summary>
    /// <remarks>
    /// Twenty-four, which is the figure docs/08-integrations.md §1 names. It is deliberately longer
    /// than the unpaid-order timeout that cancels the order: the cancellation releases the stock, and
    /// this window is what lets support re-open a conversation with a customer whose bank failed.
    /// </remarks>
    [Range(1, 168)]
    public int RetryWindowHours { get; set; } = 24;

    /// <summary>
    /// How far out of date a webhook may be before it is refused, in minutes.
    /// </summary>
    /// <remarks>
    /// Five, per docs/08-integrations.md §1. It bounds a replay of a captured signed body: without
    /// it, a payload recorded today would verify for ever.
    /// </remarks>
    [Range(1, 60)]
    public int WebhookSkewMinutes { get; set; } = 5;

    /// <summary>The largest webhook body this platform will read, in bytes.</summary>
    /// <remarks>
    /// An unauthenticated endpoint that reads a body into memory to hash it needs a ceiling, or the
    /// signature check itself becomes the denial of service.
    /// </remarks>
    [Range(1024, 1048576)]
    public int MaxWebhookBytes { get; set; } = 262_144;

    /// <summary>Whether the gateway-event processor runs in this host. Off in the API, on in the worker.</summary>
    public bool EventProcessorEnabled { get; set; }

    /// <summary>How often the processor polls, in seconds.</summary>
    [Range(1, 300)]
    public int EventPollIntervalSeconds { get; set; } = 5;

    /// <summary>How many events one pass claims.</summary>
    [Range(1, 500)]
    public int EventBatchSize { get; set; } = 25;

    /// <summary>Attempts before an event is dead-lettered and left for a human.</summary>
    [Range(1, 50)]
    public int MaxEventAttempts { get; set; } = 8;

    /// <summary>Seconds added between retries of a failing event, multiplied by the attempt count.</summary>
    [Range(1, 3600)]
    public int EventRetryBackoffSeconds { get; set; } = 30;

    /// <summary>Whether the reconciliation sweep runs in this host. Off in the API, on in the worker.</summary>
    public bool ReconciliationEnabled { get; set; }

    /// <summary>How often the sweep runs, in minutes. Fifteen, per docs/08-integrations.md §1.</summary>
    [Range(1, 1440)]
    public int ReconciliationIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// How long an unpaid collection is left alone before the sweep asks the gateway about it, in
    /// minutes.
    /// </summary>
    /// <remarks>
    /// Twenty, per docs/08-integrations.md §1. Shorter than this and the sweep is racing the shopper,
    /// who may still be reading a one-time password.
    /// </remarks>
    [Range(1, 240)]
    public int ReconciliationGraceMinutes { get; set; } = 20;

    /// <summary>How many collections one sweep examines.</summary>
    [Range(1, 500)]
    public int ReconciliationBatchSize { get; set; } = 50;

    /// <summary>Whether settlement reports are pulled in this host. Off in the API, on in the worker.</summary>
    public bool SettlementIngestionEnabled { get; set; }

    /// <summary>How often reports are pulled, in hours. Daily, per docs/08-integrations.md §1.</summary>
    [Range(1, 168)]
    public int SettlementIntervalHours { get; set; } = 24;

    /// <summary>How many days back an ingestion run looks, to catch a report that arrived late.</summary>
    [Range(1, 90)]
    public int SettlementLookbackDays { get; set; } = 3;

    /// <summary>
    /// Whether cancelling a paid order automatically refunds it.
    /// </summary>
    /// <remarks>
    /// On. A shopper whose order was cancelled after they paid is owed their money without having to
    /// ask, and the alternative — a queue an operator works through — is how refunds end up taking a
    /// fortnight. The maker-checker threshold still applies to what this raises.
    /// </remarks>
    public bool AutoRefundOnCancellation { get; set; } = true;

    /// <summary>The largest page any payment list will serve.</summary>
    [Range(1, 200)]
    public int MaxPageSize { get; set; } = 50;
}

/// <summary>
/// Razorpay's credentials and endpoint (docs/06-infrastructure-devops.md §, docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// A top-level section rather than a child of <see cref="PaymentsOptions"/>, so the environment
/// variable names are the ones docs/06 already publishes: <c>Razorpay__KeyId</c>,
/// <c>Razorpay__KeySecret</c>, <c>Razorpay__WebhookSecret</c>, <c>Razorpay__RouteEnabled</c>.
/// </para>
/// <para>
/// Every credential is blank by default, and that is the shippable state. A deployment with no
/// Razorpay account gets an adapter that reports itself unusable and an endpoint that refuses with a
/// named error — not a crash, and not a payment that exists in a response and nowhere else. Filling
/// these three values in is the whole of enabling payments.
/// </para>
/// <para>
/// The secret and the webhook secret are never logged, never returned by an endpoint and never sent
/// to a browser. <see cref="KeyId"/> alone is publishable: it is what the checkout widget is
/// initialised with, and it is in every storefront bundle by design.
/// </para>
/// </remarks>
internal sealed class RazorpayOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Razorpay";

    /// <summary>The publishable key id. Safe to send to a browser; it is what the widget needs.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>The API secret. Never leaves this process.</summary>
    public string KeySecret { get; set; } = string.Empty;

    /// <summary>The webhook signing secret. Different from the API secret, and set separately.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>The merchant account number, needed by the settlement and payout APIs.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>
    /// Whether Route is enabled on this merchant account.
    /// </summary>
    /// <remarks>
    /// Read here and acted on at Step 18. Recorded now because it is one of the four values docs/06
    /// lists, and a deployment configuring payments should not have to come back for it later.
    /// </remarks>
    public bool RouteEnabled { get; set; }

    /// <summary>The API root. Configurable so a sandbox or a proxy can be pointed at.</summary>
    [Required]
    public string BaseUrl { get; set; } = "https://api.razorpay.com/v1/";

    /// <summary>How long the gateway has to answer before a call is abandoned, in seconds.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Whether the three values a collection actually needs are all present.</summary>
    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(KeySecret);

    /// <summary>Whether webhooks can be verified. Separate: a key pair without a webhook secret is a
    /// half-configured deployment that can take money and cannot be told about it.</summary>
    public bool CanVerifyWebhooks => !string.IsNullOrWhiteSpace(WebhookSecret);
}
