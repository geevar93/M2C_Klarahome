using KlaraHome.Modules.Carts.Application.Carts;

namespace KlaraHome.Modules.Carts.Application.Checkout;

/// <summary>An address as the checkout holds it, frozen at the moment it was chosen.</summary>
/// <param name="SourceAddressId">The account address it was copied from.</param>
/// <param name="RecipientName">Who the courier asks for.</param>
/// <param name="Mobile">The delivery contact number.</param>
/// <param name="Line1">House or flat number and building.</param>
/// <param name="Line2">Street, area or locality.</param>
/// <param name="Landmark">A nearby landmark.</param>
/// <param name="City">City or town.</param>
/// <param name="StateId">The <c>platform.states</c> row.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="Gstin">A GSTIN this shipment is billed to.</param>
/// <param name="IsBusiness">Whether it is a business address.</param>
internal sealed record CheckoutAddressResponse(
    Guid SourceAddressId,
    string RecipientName,
    string Mobile,
    string Line1,
    string? Line2,
    string? Landmark,
    string City,
    Guid StateId,
    string Pincode,
    string? Gstin,
    bool IsBusiness);

/// <summary>A delivery service offered for one seller's parcel.</summary>
/// <param name="Code">The code the choice is stored as.</param>
/// <param name="Name">What the shopper sees.</param>
/// <param name="Carrier">The courier, when one has been decided.</param>
/// <param name="Amount">What it costs, inclusive of tax.</param>
/// <param name="DispatchSlaHours">How long the seller has to hand the parcel over.</param>
/// <param name="PromisedMinDays">Earliest delivery, in days from dispatch.</param>
/// <param name="PromisedMaxDays">Latest delivery, in days from dispatch.</param>
/// <param name="IsCodAvailable">Whether cash may be collected on this service.</param>
internal sealed record ShippingOptionResponse(
    string Code,
    string Name,
    string? Carrier,
    decimal Amount,
    int DispatchSlaHours,
    int PromisedMinDays,
    int PromisedMaxDays,
    bool IsCodAvailable);

/// <summary>What one seller's parcel may be sent by, and what has been chosen.</summary>
/// <param name="VendorId">The seller.</param>
/// <param name="VendorName">What they are called.</param>
/// <param name="SelectedCode">The service currently chosen, or null.</param>
/// <param name="Options">
/// What is available. An empty list means this seller does not deliver to the chosen address, which
/// is reported against them rather than failing the whole basket.
/// </param>
internal sealed record VendorShippingOptionsResponse(
    Guid VendorId,
    string VendorName,
    string? SelectedCode,
    IReadOnlyList<ShippingOptionResponse> Options);

/// <summary>A way this basket may be paid for.</summary>
/// <param name="Method">The method: <c>prepaid</c> or <c>cod</c>.</param>
/// <param name="Name">What the shopper sees.</param>
/// <param name="IsAvailable">Whether it may be chosen.</param>
/// <param name="Fee">The handling fee it attracts, inclusive of tax.</param>
/// <param name="Reason">Why it may not be chosen, or null when it may.</param>
internal sealed record PaymentMethodResponse(
    string Method,
    string Name,
    bool IsAvailable,
    decimal Fee,
    string? Reason);

/// <summary>A checkout session, as the API states it.</summary>
/// <param name="Id">The session.</param>
/// <param name="CartId">The basket being paid for.</param>
/// <param name="Status">How far through the shopper has got.</param>
/// <param name="CurrencyCode">ISO 4217 code every figure is in.</param>
/// <param name="ShippingAddress">Where it goes, once chosen.</param>
/// <param name="BillingAddress">Who it is billed to, once chosen.</param>
/// <param name="Gstin">The GSTIN the invoice will be raised against.</param>
/// <param name="PaymentMethod">How it is being paid for.</param>
/// <param name="Shipments">The chosen delivery service per seller.</param>
/// <param name="Cart">The basket, priced and validated exactly as the cart page shows it.</param>
/// <param name="ExpiresAt">When the attempt lapses.</param>
/// <param name="OrderNumber">The order it became, once placed.</param>
internal sealed record CheckoutResponse(
    Guid Id,
    Guid CartId,
    string Status,
    string CurrencyCode,
    CheckoutAddressResponse? ShippingAddress,
    CheckoutAddressResponse? BillingAddress,
    string? Gstin,
    string PaymentMethod,
    IReadOnlyList<VendorShippingOptionsResponse> Shipments,
    CartResponse Cart,
    DateTimeOffset ExpiresAt,
    string? OrderNumber);

/// <summary>What the storefront needs to start a payment, or null when there is nothing to pay now.</summary>
/// <param name="Provider">The gateway.</param>
/// <param name="ProviderOrderId">The gateway's own handle on the payment.</param>
/// <param name="PublicKey">The publishable key the checkout widget is initialised with.</param>
/// <param name="Amount">What the gateway is being asked for.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
internal sealed record PaymentResponse(
    string Provider,
    string ProviderOrderId,
    string PublicKey,
    decimal Amount,
    string CurrencyCode);

/// <summary>The result of placing an order (docs/04-api-specification.md §3.3).</summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">The human-readable number a shopper quotes to support.</param>
/// <param name="Status">Where the order starts.</param>
/// <param name="Payment">How to pay, or null for cash on delivery.</param>
internal sealed record PlaceOrderResponse(
    Guid OrderId,
    string OrderNumber,
    string Status,
    PaymentResponse? Payment);
