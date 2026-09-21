using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Shipping.Infrastructure.Courier;

/// <summary>The provider names this platform knows, as they are stored on a shipment.</summary>
/// <remarks>
/// Strings rather than an enum, because they are persisted on every consignment and read by an
/// operator. A second aggregator is a new adapter and a new constant, never a schema change.
/// </remarks>
internal static class ShippingProviders
{
    /// <summary>
    /// Shiprocket: the v1 courier, one integration and many carriers (docs/08-integrations.md §2).
    /// </summary>
    /// <remarks>
    /// The key an adapter publishes and a shipment stores, and the value
    /// <c>Shipping:Provider</c> takes to select it. It is the only place in this codebase a courier
    /// is named — a second aggregator is a new adapter and a new constant here, never a change to a
    /// caller, a domain type or the schema (ADR-018).
    /// </remarks>
    public const string Shiprocket = "shiprocket";

    /// <summary>
    /// What Step 16 called the configured aggregator, before one was chosen.
    /// </summary>
    /// <remarks>
    /// Kept as an <b>alias</b> and not as a selector: configuration or a parcel written before
    /// ADR-018 says <c>aggregator</c>, means "whichever aggregator this deployment has", and must
    /// keep resolving. Nothing new is ever stored under it.
    /// </remarks>
    public const string Aggregator = "aggregator";

    /// <summary>
    /// No aggregator at all. An operator types the air waybill their courier gave them.
    /// </summary>
    /// <remarks>
    /// A provider with nothing behind it, deliberately — the same shape as cash on delivery in the
    /// payments module. It is what makes this module usable on the day it ships, and it is the
    /// documented fallback when a booking fails (docs/08-integrations.md §2).
    /// </remarks>
    public const string Manual = "manual";
}

/// <summary>What a courier says about a destination.</summary>
/// <param name="Courier">The courier it would use, or its own name where it does not say.</param>
/// <param name="PrepaidOk">Whether a prepaid parcel can be delivered.</param>
/// <param name="CodOk">Whether cash can be collected. Frequently false where prepaid is true.</param>
/// <param name="PickupOk">Whether a reverse pickup can be collected, for a return.</param>
/// <param name="EtaDays">How long it says it takes, or null where it does not say.</param>
/// <param name="MaxWeightGrams">The heaviest parcel it will take, or null for no stated limit.</param>
/// <param name="City">The city the courier says the PIN code is, where it says.</param>
/// <param name="State">The state the courier says it is in, where it says.</param>
internal sealed record CourierServiceability(
    string Courier,
    bool PrepaidOk,
    bool CodOk,
    bool PickupOk,
    int? EtaDays,
    int? MaxWeightGrams,
    string? City = null,
    string? State = null);

/// <summary>A route to be priced by the courier itself.</summary>
/// <param name="OriginPincode">Where the parcel is collected from: the seller's pickup address.</param>
/// <param name="DestinationPincode">Where it is going.</param>
/// <param name="WeightGrams">The weight it will be priced on.</param>
/// <param name="DeclaredValue">What the goods are worth, which some couriers price insurance on.</param>
/// <param name="IsCod">Whether cash will be collected, which rules out couriers that will not.</param>
internal sealed record CourierRateRequest(
    string OriginPincode,
    string DestinationPincode,
    int WeightGrams,
    decimal DeclaredValue,
    bool IsCod);

/// <summary>What one courier would charge to carry a parcel on a route.</summary>
/// <param name="Courier">The courier's name, as the aggregator spells it.</param>
/// <param name="CourierId">The aggregator's id for that courier, where it has one.</param>
/// <param name="Freight">
/// The freight charge, excluding any cash-collection charge. Whether tax is inside it is the
/// aggregator's convention, and <c>Shipping:AggregatorRatesIncludeTax</c> records which.
/// </param>
/// <param name="EtaDays">Days in transit, as the courier estimates, or null where it does not say.</param>
/// <param name="CodOk">Whether this courier will collect cash on this route.</param>
/// <param name="IsRecommended">
/// Whether the aggregator would pick this courier itself when a parcel is booked without naming one.
/// </param>
internal sealed record CourierRate(
    string Courier,
    string? CourierId,
    decimal Freight,
    int? EtaDays,
    bool CodOk,
    bool IsRecommended);

/// <summary>An address a parcel leaves from or goes to, in the shape a courier API wants it.</summary>
/// <param name="Name">Who is asked for at the door.</param>
/// <param name="Phone">The number the courier rings.</param>
/// <param name="Line1">Building and unit.</param>
/// <param name="Line2">Street, area or locality.</param>
/// <param name="Landmark">A nearby landmark. Couriers in India navigate by these.</param>
/// <param name="City">City or town.</param>
/// <param name="StateName">The state, spelled the way the courier expects it.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="LocationCode">The courier's own id for a registered pickup address, where there is one.</param>
internal sealed record CourierAddress(
    string Name,
    string? Phone,
    string Line1,
    string? Line2,
    string? Landmark,
    string City,
    string? StateName,
    string Pincode,
    string? LocationCode = null);

/// <summary>One line of a booking, as a courier's paperwork lists it.</summary>
/// <param name="Sku">The stock-keeping unit.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Quantity">How many units.</param>
/// <param name="DeclaredValue">What they are worth.</param>
internal sealed record CourierParcelItem(string Sku, string Name, int Quantity, decimal DeclaredValue);

/// <summary>What to book.</summary>
/// <param name="ShipmentId">Our own id for the consignment, sent so the courier echoes it back.</param>
/// <param name="Reference">The order number, which is what appears on the courier's dashboard.</param>
/// <param name="Pickup">Where it is collected from.</param>
/// <param name="Destination">Where it is going.</param>
/// <param name="WeightGrams">The chargeable weight.</param>
/// <param name="Dimensions">The box.</param>
/// <param name="DeclaredValue">What the goods are worth, for insurance and paperwork.</param>
/// <param name="CodAmount">Cash to collect at the door, or null for a prepaid parcel.</param>
/// <param name="CurrencyCode">ISO 4217 code the amounts are in.</param>
/// <param name="Method">Standard or express, as the shopper chose.</param>
/// <param name="IsReturn">Whether this is a reverse pickup.</param>
/// <param name="Items">What is in it.</param>
internal sealed record CourierBookingRequest(
    Guid ShipmentId,
    string Reference,
    CourierAddress Pickup,
    CourierAddress Destination,
    int WeightGrams,
    ShipmentDimensions Dimensions,
    decimal DeclaredValue,
    decimal? CodAmount,
    string CurrencyCode,
    ShippingMethod Method,
    bool IsReturn,
    IReadOnlyList<CourierParcelItem> Items);

/// <summary>What the courier gave back.</summary>
/// <param name="Courier">The courier actually carrying it.</param>
/// <param name="ServiceName">Their name for the service, kept verbatim.</param>
/// <param name="Awb">The air waybill.</param>
/// <param name="ProviderShipmentId">The aggregator's own id, for every later call about it.</param>
/// <param name="TrackingUrl">Where a shopper can watch it.</param>
/// <param name="ExpectedDeliveryAt">Their promise, where they make one.</param>
/// <param name="FreightCost">What they say they will charge.</param>
/// <param name="LabelUrl">Where the label PDF can be fetched, where they publish one.</param>
internal sealed record CourierBooking(
    string Courier,
    string? ServiceName,
    string Awb,
    string? ProviderShipmentId,
    string? TrackingUrl,
    DateTimeOffset? ExpectedDeliveryAt,
    decimal? FreightCost,
    string? LabelUrl);

/// <summary>One scan, as the adapter has translated it.</summary>
/// <param name="ProviderEventId">The courier's id for the scan, or a synthesised one.</param>
/// <param name="Status">What this platform decided it means.</param>
/// <param name="CourierStatus">The courier's own word, verbatim.</param>
/// <param name="Location">Where it happened.</param>
/// <param name="Remark">What the courier wrote. On a failed attempt this is the reason.</param>
/// <param name="NdrReason">What this platform made of that reason, on a failed attempt.</param>
/// <param name="OccurredAt">When the courier says it happened.</param>
/// <param name="Raw">The courier's payload for this scan.</param>
internal sealed record CourierScan(
    string ProviderEventId,
    ShipmentStatus Status,
    string? CourierStatus,
    string? Location,
    string? Remark,
    NdrReasonCode? NdrReason,
    DateTimeOffset OccurredAt,
    string? Raw);

/// <summary>Everything a courier currently says about one parcel.</summary>
/// <param name="Awb">The air waybill.</param>
/// <param name="Status">Where they say it is now.</param>
/// <param name="ChargedWeightGrams">What they billed for, where they say.</param>
/// <param name="FreightCost">What they charged, where they say.</param>
/// <param name="ExpectedDeliveryAt">Their current promise.</param>
/// <param name="Scans">Its history, oldest first.</param>
internal sealed record CourierTracking(
    string Awb,
    ShipmentStatus Status,
    int? ChargedWeightGrams,
    decimal? FreightCost,
    DateTimeOffset? ExpectedDeliveryAt,
    IReadOnlyList<CourierScan> Scans);

/// <summary>What a webhook turned out to be about, once its body was parsed.</summary>
/// <param name="ProviderEventId">The courier's id for the event. The replay-protection key.</param>
/// <param name="EventType">What happened, in the courier's vocabulary.</param>
/// <param name="Awb">The air waybill it concerns, where the payload names one.</param>
/// <param name="ShipmentId">Our own consignment id, echoed back where we sent one.</param>
/// <param name="OccurredAt">When the courier says it happened.</param>
/// <param name="Scans">The scans the payload carried.</param>
internal sealed record CourierWebhookEnvelope(
    string ProviderEventId,
    string EventType,
    string? Awb,
    Guid? ShipmentId,
    DateTimeOffset? OccurredAt,
    IReadOnlyList<CourierScan> Scans);

/// <summary>What a handover produced.</summary>
/// <param name="ProviderManifestId">The aggregator's id for it, where it issues one.</param>
/// <param name="DocumentUrl">Where the sheet can be fetched, where it publishes one.</param>
internal sealed record CourierManifest(string? ProviderManifestId, string? DocumentUrl);

/// <summary>
/// The logistics aggregator, behind an interface this platform owns (docs/08-integrations.md §2).
/// </summary>
/// <remarks>
/// <para>
/// No vendor type crosses this line, which is what keeps the product redistributable: a client on a
/// different aggregator writes one adapter, and nothing in the domain, the endpoints or the jobs
/// changes. It is also why the interface speaks this platform's vocabulary —
/// <see cref="ShipmentStatus"/>, not <c>"OFD"</c> — and why translating a courier's status words is
/// the adapter's job rather than a handler's.
/// </para>
/// <para>
/// Everything returns a <see cref="Result{T}"/> rather than throwing. An aggregator being
/// unreachable is an ordinary outcome here, not an exception: the sub-order stays packed, the parcel
/// appears in the exception queue with a manual-AWB fallback, and nothing that discovered the
/// failure is taken down by it.
/// </para>
/// <para>
/// <see cref="IsConfigured"/> is what lets this module ship before its credentials exist. An
/// unconfigured adapter is registered, is honest about being unusable, and every path that needs it
/// falls back to the manual adapter or answers a named 503.
/// </para>
/// </remarks>
internal interface IShippingProvider
{
    /// <summary>Which provider this is, as it is stored on a shipment.</summary>
    string Name { get; }

    /// <summary>Whether this deployment has the credentials to actually use it.</summary>
    bool IsConfigured { get; }

    /// <summary>Whether it can verify a webhook. Separate: a key without a webhook secret is half a deployment.</summary>
    bool CanVerifyWebhooks { get; }

    /// <summary>Asks what can be delivered to a PIN code, and how fast.</summary>
    /// <param name="pincode">The six-digit destination.</param>
    /// <param name="pickupPincode">Where it would leave from, when the caller knows.</param>
    /// <param name="weightGrams">The parcel's chargeable weight.</param>
    /// <param name="isCod">Whether cash would be collected at the door.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<CourierServiceability>> CheckServiceabilityAsync(
        string pincode,
        string? pickupPincode,
        int weightGrams,
        bool isCod,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks what each courier would charge to carry a parcel on a route, right now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The live half of pricing delivery, used when <c>Shipping:ChargeSource</c> is
    /// <c>aggregator</c>: checkout asks this and passes the courier's price on at cost. A failure
    /// means "could not ask" and the caller falls back to the rate card; an empty success means
    /// "asked, and nobody will carry it", which is an answer and must not be papered over.
    /// </para>
    /// <para>
    /// An adapter with no rate API returns <see cref="Application.ShippingErrors.ProviderUnavailable"/>.
    /// </para>
    /// </remarks>
    /// <param name="request">The route and the parcel.</param>
    /// <param name="cancellationToken">Cancellation token. The caller bounds this call tightly.</param>
    Task<Result<IReadOnlyList<CourierRate>>> QuoteRatesAsync(
        CourierRateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Books a consignment and gets an air waybill for it.</summary>
    /// <param name="request">What to book.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<CourierBooking>> CreateShipmentAsync(
        CourierBookingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the courier's own label for a consignment.
    /// </summary>
    /// <remarks>
    /// Returns the PDF bytes, or a failure when the aggregator has none. A failure is not fatal: the
    /// caller renders this platform's own 4×6 label instead, which every Indian courier accepts.
    /// </remarks>
    /// <param name="providerShipmentId">The aggregator's id for the consignment.</param>
    /// <param name="awb">Its air waybill.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<byte[]>> GenerateLabelAsync(
        string? providerShipmentId,
        string awb,
        CancellationToken cancellationToken = default);

    /// <summary>Tells the aggregator a batch of parcels is being handed over.</summary>
    /// <param name="awbs">The air waybills on the sheet.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<CourierManifest>> GenerateManifestAsync(
        IReadOnlyList<string> awbs,
        CancellationToken cancellationToken = default);

    /// <summary>Asks the courier to collect.</summary>
    /// <param name="providerShipmentId">The aggregator's id for the consignment.</param>
    /// <param name="awb">Its air waybill.</param>
    /// <param name="pickupAt">When the parcel will be ready.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<DateTimeOffset>> SchedulePickupAsync(
        string? providerShipmentId,
        string awb,
        DateTimeOffset pickupAt,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels a booking the courier has not yet collected.</summary>
    /// <param name="providerShipmentId">The aggregator's id for the consignment.</param>
    /// <param name="awb">Its air waybill.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> CancelShipmentAsync(
        string? providerShipmentId,
        string awb,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the courier to bring a parcel back to the seller instead of trying to deliver it again.
    /// </summary>
    /// <remarks>
    /// Aggregators accept this only as the answer to a failed delivery attempt — there is no call
    /// that recalls a parcel in transit — so callers make it while the parcel is in
    /// <see cref="Domain.ShipmentStatus.Exception"/>.
    /// </remarks>
    /// <param name="awb">The air waybill.</param>
    /// <param name="remark">Why, for the courier's record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result> ReturnToOriginAsync(
        string awb,
        string? remark,
        CancellationToken cancellationToken = default);

    /// <summary>Re-reads a consignment from the courier. The polling fallback for a lost webhook.</summary>
    /// <param name="awb">The air waybill.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<CourierTracking>> TrackAsync(string awb, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a webhook against the raw body.
    /// </summary>
    /// <remarks>
    /// The <em>raw</em> body, before any deserialisation: a re-serialised document is a different
    /// byte sequence and would never verify. Constant-time comparison, so a wrong signature takes the
    /// same time to reject as a nearly-right one.
    /// </remarks>
    /// <param name="rawBody">The bytes as they arrived.</param>
    /// <param name="signature">The signature header.</param>
    bool VerifyWebhookSignature(string rawBody, string? signature);

    /// <summary>Reads what a webhook is about, without acting on it.</summary>
    /// <param name="rawBody">The bytes as they arrived.</param>
    Result<CourierWebhookEnvelope> ReadWebhook(string rawBody);
}
