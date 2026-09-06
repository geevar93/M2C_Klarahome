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
}
