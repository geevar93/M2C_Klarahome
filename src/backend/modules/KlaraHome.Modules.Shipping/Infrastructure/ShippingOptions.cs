using System.ComponentModel.DataAnnotations;
using KlaraHome.Modules.Shipping.Infrastructure.Quoting;

namespace KlaraHome.Modules.Shipping.Infrastructure;

/// <summary>
/// How this deployment moves parcels (docs/06-infrastructure-devops.md §4.1).
/// </summary>
/// <remarks>
/// <para>
/// The credentials sit on this options class rather than in a section of their own — the shape
/// docs/06 already publishes is <c>Shipping__Provider</c>, <c>Shipping__ApiKey</c> and
/// <c>Shipping__WebhookSecret</c>, and inventing a second name for a variable an operator has
/// already been told about would be a documentation bug with a deployment failure attached.
/// </para>
/// <para>
/// Every credential is blank by default, and that is the shippable state. With none of them the
/// aggregator adapter reports itself unusable, and dispatch falls back to the manual adapter: an
/// operator types the air waybill their courier gave them over the counter, and everything
/// downstream — tracking entered by hand, the order timeline, cash reconciliation — works exactly as
/// it does with an aggregator. A marketplace can trade on that from day one.
/// </para>
/// <para>
/// The rest is the platform's own policy: how long a serviceability answer stays usable, how long a
/// silence from a courier is too long, and what a bulky parcel is measured by. None of it is a
/// shopkeeper's decision, so none of it is in store settings.
/// </para>
/// </remarks>
internal sealed class ShippingOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Shipping";

    /// <summary>
    /// Which adapter books consignments, by its key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>shiprocket</c> in v1 (ADR-018), and <b>the only place in this deployment a courier is
    /// named</b>. The registry resolves this against the adapters DI registered, so switching
    /// courier is this one value.
    /// </para>
    /// <para>
    /// Blank — or a key this build has no adapter for, including a typo — means the manual adapter.
    /// That is what a deployment with no courier account should use, it is why this module can ship
    /// before its credentials exist, and it is deliberately a degradation rather than a failure: a
    /// misspelt provider leaves parcels bookable by hand instead of taking fulfilment down.
    /// </para>
    /// </remarks>
    [StringLength(32)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>The aggregator's API key or bearer token. Never leaves this process.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The API user whose credentials are exchanged for a token, where the aggregator works that way.
    /// </summary>
    /// <remarks>
    /// Aggregator tokens in this class of API expire in days rather than years, so a deployment
    /// configured with a token alone stops booking parcels the week after it is set up. Supplying an
    /// API user instead lets the adapter fetch a fresh token when the one it holds is refused —
    /// which is the difference between an integration that works and one that works until somebody
    /// goes on holiday.
    /// </remarks>
    public string ApiUser { get; set; } = string.Empty;

    /// <summary>That user's secret. Never leaves this process.</summary>
    public string ApiSecret { get; set; } = string.Empty;

    /// <summary>
    /// The webhook signing secret. Set separately from the API key, and deliberately so.
    /// </summary>
    /// <remarks>
    /// A deployment with an API key and no webhook secret can book parcels and cannot be told what
    /// happens to them — which is a half-configured state worth being able to detect, and is why
    /// the adapter reports the two capabilities separately.
    /// </remarks>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>The API root. Configurable so a sandbox or a proxy can be pointed at.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Where serviceability and rate questions go, when the aggregator answers them from a host of
    /// their own. Blank means <see cref="BaseUrl"/>.
    /// </summary>
    /// <remarks>
    /// Shiprocket's sandbox splits in two: sign-in and bookings on <c>api-sandbox.shiprocket.in</c>,
    /// serviceability and rates on <c>serviceability-sandbox.shiprocket.in</c>, both taking the token
    /// the first one issues. Production answers everything from <c>apiv2.shiprocket.in</c> and leaves
    /// this blank. Like <see cref="BaseUrl"/> it is also the outbound allow-list, and must be https.
    /// </remarks>
    public string ServiceabilityBaseUrl { get; set; } = string.Empty;

    /// <summary>How long the aggregator has to answer before a call is abandoned, in seconds.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>The largest webhook body this platform will read, in bytes.</summary>
    /// <remarks>
    /// An unauthenticated endpoint that reads a body into memory to hash it needs a ceiling, or the
    /// signature check itself becomes the denial of service.
    /// </remarks>
    [Range(1024, 1048576)]
    public int MaxWebhookBytes { get; set; } = 262_144;

    /// <summary>How far out of date a webhook may be before it is stored and ignored, in minutes.</summary>
    [Range(1, 1440)]
    public int WebhookSkewMinutes { get; set; } = 60;

    /// <summary>
    /// The divisor a volumetric weight is computed with.
    /// </summary>
    /// <remarks>
    /// Five thousand is the Indian convention — <c>L × W × H ÷ 5000</c> in centimetres and
    /// kilograms — and it is the first number an aggregator negotiates, which is why it is
    /// configuration rather than a constant.
    /// </remarks>
    [Range(1000, 10000)]
    public int VolumetricDivisor { get; set; } = 5000;

    /// <summary>
    /// The GST percentage applied to freight, for the tax figure shown beside a delivery charge.
    /// </summary>
    /// <remarks>
    /// Eighteen, which is the rate on courier services in India. It is used only to say how much tax
    /// is inside a quoted delivery charge; the split that reaches an invoice is the pricing engine's,
    /// computed from the same inclusive figure, so the two cannot disagree about the total.
    /// </remarks>
    [Range(0, 100)]
    public decimal FreightGstRate { get; set; } = 18m;

    /// <summary>
    /// Where the delivery charge a shopper pays comes from, by its key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>aggregator</c> (the default): the configured courier is asked for its live price at
    /// checkout and it is passed on at cost. Whenever it cannot answer — no courier configured,
    /// the seller has no pickup address, the call fails or runs past
    /// <see cref="LiveRateTimeoutMilliseconds"/> — the rate card answers instead, so a courier
    /// outage never stops a checkout.
    /// </para>
    /// <para>
    /// <c>ratecard</c>: the platform's own zones and bands alone, and the aggregator is never asked.
    /// Blank or unknown means <c>aggregator</c>, which already degrades to the rate card by itself.
    /// </para>
    /// </remarks>
    [StringLength(32)]
    public string ChargeSource { get; set; } = DeliveryChargeSources.Aggregator;

    /// <summary>How long checkout waits for a courier's live price before using the rate card, in milliseconds.</summary>
    [Range(250, 20_000)]
    public int LiveRateTimeoutMilliseconds { get; set; } = 3_000;

    /// <summary>How long a live price is reused for the same route and parcel, in seconds.</summary>
    /// <remarks>
    /// Long enough that the price shown when delivery options load is the price recorded when one is
    /// chosen a moment later; short enough that a courier's tariff change reaches the next shopper.
    /// Zero turns the reuse off.
    /// </remarks>
    [Range(0, 3_600)]
    public int LiveRateCacheSeconds { get; set; } = 300;

    /// <summary>
    /// Whether the aggregator's quoted freight already includes GST.
    /// </summary>
    /// <remarks>
    /// False adds <see cref="FreightGstRate"/> on top, so the shopper pays what the courier will
    /// actually bill. <b>To be confirmed against a sandbox invoice</b> before going live: getting it
    /// wrong either over-charges every shopper by the tax or absorbs it on every parcel.
    /// </remarks>
    public bool AggregatorRatesIncludeTax { get; set; }

    /// <summary>
    /// The weight a parcel is priced on when its products carry no weight, in grams.
    /// </summary>
    /// <remarks>
    /// Half a kilogram, which is the smallest slab Indian couriers bill. A product without a weight is
    /// a catalogue gap, and pricing it as weightless would quote a figure no courier charges.
    /// </remarks>
    [Range(1, 500_000)]
    public int DefaultParcelWeightGrams { get; set; } = 500;

    /// <summary>How long a cached serviceability answer stays usable, in hours.</summary>
    /// <remarks>
    /// Twenty-four. A PIN code that became serviceable this morning is worth knowing about tomorrow;
    /// asking an aggregator on the hot path to find out a day sooner is how a storefront acquires a
    /// dependency on somebody else's uptime (docs/08-integrations.md §2).
    /// </remarks>
    [Range(1, 720)]
    public int ServiceabilityTtlHours { get; set; } = 24;

    /// <summary>Whether the nightly serviceability refresh runs in this host. Off in the API, on in the worker.</summary>
    public bool ServiceabilityRefreshEnabled { get; set; }

    /// <summary>How often the refresh runs, in hours. Nightly, per docs/08-integrations.md §2.</summary>
    [Range(1, 168)]
    public int ServiceabilityRefreshIntervalHours { get; set; } = 24;

    /// <summary>How many PIN codes one refresh pass asks about.</summary>
    [Range(1, 5000)]
    public int ServiceabilityRefreshBatchSize { get; set; } = 200;

    /// <summary>Whether the courier-event worker runs in this host. Off in the API, on in the worker.</summary>
    public bool EventProcessorEnabled { get; set; }

    /// <summary>How often it polls, in seconds.</summary>
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

    /// <summary>Whether the tracking-polling fallback runs in this host. Off in the API, on in the worker.</summary>
    public bool TrackingPollEnabled { get; set; }

    /// <summary>How often the fallback runs, in minutes. Thirty, per docs/08-integrations.md §2.</summary>
    [Range(1, 1440)]
    public int TrackingPollIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// How long a silence from a courier is too long, in hours.
    /// </summary>
    /// <remarks>
    /// Twenty-four, per docs/08-integrations.md §2. A booked parcel nobody has heard about in a day
    /// is one whose webhooks are not arriving, and asking the courier directly is cheaper than
    /// finding out from the customer.
    /// </remarks>
    [Range(1, 168)]
    public int TrackingSilenceHours { get; set; } = 24;

    /// <summary>How many silent parcels one pass asks about.</summary>
    [Range(1, 500)]
    public int TrackingPollBatchSize { get; set; } = 50;

    /// <summary>
    /// Whether the sweep that retries a courier cancellation runs in this host. Off in the API, on
    /// in the worker.
    /// </summary>
    public bool CourierCancellationRetryEnabled { get; set; }

    /// <summary>
    /// How often a cancelled order's still-booked parcel is offered to its courier again, in minutes.
    /// </summary>
    /// <remarks>
    /// Short, because the retry is racing a pickup: every pass that fails is another chance for a
    /// driver to arrive for a parcel the seller will not hand over.
    /// </remarks>
    [Range(1, 1440)]
    public int CourierCancellationRetryIntervalMinutes { get; set; } = 5;

    /// <summary>How many waiting parcels one pass retries.</summary>
    [Range(1, 500)]
    public int CourierCancellationRetryBatchSize { get; set; } = 50;

    /// <summary>
    /// Whether a confirmed seller's part opens a draft consignment by itself.
    /// </summary>
    /// <remarks>
    /// On. The alternative is a packer who has to create the parcel before packing it, which is a
    /// step nobody would ever want and one that would leave "what is waiting to be packed"
    /// unanswerable. Off is for a deployment that fulfils entirely outside this platform.
    /// </remarks>
    public bool AutoDraftOnConfirmation { get; set; } = true;

    /// <summary>
    /// Whether booking an air waybill also asks the courier to collect.
    /// </summary>
    /// <remarks>
    /// Off by default. Most sellers hand parcels over on a daily manifest rather than a per-parcel
    /// collection, and a pickup request per shipment is how a courier's operations team learns to
    /// ignore them.
    /// </remarks>
    public bool AutoSchedulePickup { get; set; }

    /// <summary>The largest page any shipping list will serve.</summary>
    [Range(1, 200)]
    public int MaxPageSize { get; set; } = 50;

    /// <summary>Whether a courier's API is configured well enough to book a parcel.</summary>
    /// <remarks>
    /// Either credential form will do: a long-lived token in <see cref="ApiKey"/>, or an API user
    /// the adapter exchanges for one. What is not optional is the base URL — it is both the endpoint
    /// and the outbound allow-list, and an adapter with credentials and nowhere to send them is not
    /// configured, it is dangerous.
    /// </remarks>
    public bool HasCourierApi
        => !string.IsNullOrWhiteSpace(Provider)
           && !string.IsNullOrWhiteSpace(BaseUrl)
           && (!string.IsNullOrWhiteSpace(ApiKey)
               || (!string.IsNullOrWhiteSpace(ApiUser) && !string.IsNullOrWhiteSpace(ApiSecret)));

    /// <summary>Whether courier webhooks can be verified.</summary>
    public bool CanVerifyWebhooks => !string.IsNullOrWhiteSpace(WebhookSecret);
}
