namespace KlaraHome.Contracts.Shipping;

/// <summary>One seller's parcel, as the caller knows it before a rate has been asked for.</summary>
/// <param name="VendorId">The seller who will dispatch it.</param>
/// <param name="PickupPincode">Where it leaves from, when the caller knows. Null lets the quoter decide.</param>
/// <param name="DestinationPincode">Where it is going.</param>
/// <param name="DestinationStateId">The <c>platform.states</c> row of the destination.</param>
/// <param name="WeightGrams">Total dead weight of the lines, which is what a courier prices on.</param>
/// <param name="ItemsTotal">What the lines come to, for a free-shipping threshold and for COD risk.</param>
/// <param name="IsCod">Whether the parcel will be collected on delivery, which some services refuse.</param>
public sealed record ShipmentQuoteRequest(
    Guid VendorId,
    string? PickupPincode,
    string DestinationPincode,
    Guid? DestinationStateId,
    int WeightGrams,
    decimal ItemsTotal,
    bool IsCod);

/// <summary>A way one seller's parcel can be sent, and what it would cost.</summary>
/// <param name="Code">
/// The stable identifier the shopper's choice is stored as. A code rather than an id, because a
/// rate card is reissued and a saved checkout must not point at a row that has been replaced.
/// </param>
/// <param name="Name">What the shopper sees: "Standard", "Express".</param>
/// <param name="Carrier">The courier, or null while none has been chosen.</param>
/// <param name="Amount">What it costs, inclusive of tax.</param>
/// <param name="TaxAmount">The tax inside that figure.</param>
/// <param name="DispatchSlaHours">How long the seller has to hand the parcel over.</param>
/// <param name="PromisedMinDays">Earliest delivery, in days from dispatch.</param>
/// <param name="PromisedMaxDays">Latest delivery, in days from dispatch.</param>
/// <param name="IsCodAvailable">Whether cash on delivery may be collected on this service.</param>
public sealed record ShippingOption(
    string Code,
    string Name,
    string? Carrier,
    decimal Amount,
    decimal TaxAmount,
    int DispatchSlaHours,
    int PromisedMinDays,
    int PromisedMaxDays,
    bool IsCodAvailable);

/// <summary>
/// What one seller's parcel may be sent by (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The seam between checkout and logistics. A checkout has to offer a delivery choice and a
/// promised date per seller, and it may not read a rate card, ask an aggregator or know what a
/// courier is — so it asks this.
/// </para>
/// <para>
/// It is declared at Step 13 and implemented for real at Step 16. Until then the Cart module
/// registers a degenerate implementation that offers one free standard service at the seller's own
/// dispatch SLA, which is the same honest position <c>QuoteRequest.ShippingAmount</c> already takes:
/// a platform with no logistics integration charges nothing for delivery rather than inventing a
/// figure. Registration is <c>TryAdd</c>, so the Shipping module simply replaces it.
/// </para>
/// </remarks>
public interface IShippingOptions
{
    /// <summary>
    /// The services available for one seller's parcel, cheapest first. An empty list means the
    /// destination cannot be served, which a checkout reports against that seller rather than
    /// failing the whole basket.
    /// </summary>
    /// <param name="request">The parcel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<ShippingOption>> QuoteAsync(
        ShipmentQuoteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether an address can be delivered to at all, before any parcel is priced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added at Step 16A (ADR-018). It answers the two questions an address has to pass — will this
    /// store sell there, and can a courier reach it — and says which one refused, because they are
    /// different facts with different remedies.
    /// </para>
    /// <para>
    /// On this seam rather than on a new contract, because Cart and Orders already hold it and the
    /// question belongs to whoever owns delivery. It reads a cached table and a settings row; it
    /// never calls a courier.
    /// </para>
    /// </remarks>
    /// <param name="pincode">The six-digit destination.</param>
    /// <param name="isCod">Whether cash would be collected at the door.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<DeliveryCheck> CheckDestinationAsync(
        string pincode,
        bool isCod = false,
        CancellationToken cancellationToken = default);
}

/// <summary>Why a destination cannot be delivered to.</summary>
public enum DeliveryRefusal
{
    /// <summary>It can. Nothing refused it.</summary>
    None = 0,

    /// <summary>
    /// The store has decided not to sell there.
    /// </summary>
    /// <remarks>
    /// Reversible in a settings screen, and therefore worth telling a shopper about in the
    /// operator's own words. Reported as <c>DELIVERY_AREA_NOT_COVERED</c>.
    /// </remarks>
    NotCovered = 1,

    /// <summary>
    /// No courier will carry a parcel there.
    /// </summary>
    /// <remarks>
    /// A fact about India's logistics rather than a decision, and nothing an operator can change.
    /// Reported as <c>PINCODE_NOT_SERVICEABLE</c>.
    /// </remarks>
    NotServiceable = 2,

    /// <summary>A courier will go there but will not collect cash. Prepaid is still offered.</summary>
    CodUnavailable = 3,
}

/// <summary>What this platform can promise about one address.</summary>
/// <param name="Pincode">The six-digit destination.</param>
/// <param name="Deliverable">Whether an order to it may be created at all.</param>
/// <param name="Covered">Whether the store's own delivery area includes it.</param>
/// <param name="Serviceable">Whether a courier will carry a parcel there.</param>
/// <param name="CodAvailable">Whether cash can be collected there.</param>
/// <param name="City">The city, from reference data where there is any.</param>
/// <param name="State">The state, likewise.</param>
/// <param name="EtaDays">How long a courier says it takes, when one says.</param>
/// <param name="Refusal">Which check refused it, or <see cref="DeliveryRefusal.None"/>.</param>
/// <param name="Message">
/// What to tell the shopper. The operator's own words for a coverage refusal, so that a store which
/// delivers to one city can say so in a sentence it chose.
/// </param>
public sealed record DeliveryCheck(
    string Pincode,
    bool Deliverable,
    bool Covered,
    bool Serviceable,
    bool CodAvailable,
    string? City,
    string? State,
    int? EtaDays,
    DeliveryRefusal Refusal,
    string? Message);
