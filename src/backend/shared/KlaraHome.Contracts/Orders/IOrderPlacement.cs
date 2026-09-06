using KlaraHome.Contracts.Pricing;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Contracts.Orders;

/// <summary>An address frozen onto an order, as the checkout captured it.</summary>
/// <param name="RecipientName">Who the courier asks for.</param>
/// <param name="Mobile">The delivery contact number in E.164.</param>
/// <param name="Line1">House or flat number and building.</param>
/// <param name="Line2">Street, area or locality.</param>
/// <param name="Landmark">A nearby landmark.</param>
/// <param name="City">City or town.</param>
/// <param name="StateId">The <c>platform.states</c> row.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="Gstin">The GSTIN this shipment is billed to, for a B2B invoice.</param>
public sealed record OrderAddress(
    string RecipientName,
    string Mobile,
    string Line1,
    string? Line2,
    string? Landmark,
    string City,
    Guid StateId,
    string Pincode,
    string? Gstin);

/// <summary>What one seller's part of the order will be shipped by.</summary>
/// <param name="VendorId">The seller.</param>
/// <param name="OptionCode">The service the shopper chose.</param>
/// <param name="Carrier">The courier, when one has been decided.</param>
/// <param name="Amount">What it costs, inclusive of tax.</param>
/// <param name="DispatchSlaHours">How long the seller has to hand the parcel over.</param>
/// <param name="PromisedMinDays">Earliest delivery, in days from dispatch.</param>
/// <param name="PromisedMaxDays">Latest delivery, in days from dispatch.</param>
public sealed record OrderShipmentPlan(
    Guid VendorId,
    string OptionCode,
    string? Carrier,
    decimal Amount,
    int DispatchSlaHours,
    int PromisedMinDays,
    int PromisedMaxDays);

/// <summary>Everything Orders needs to create an order, decided and priced by the checkout.</summary>
/// <remarks>
/// The quote is passed in rather than recomputed. It has already been shown to the shopper and
/// agreed to, and a second calculation between the review screen and the order is the one way the
/// confirmation ends up carrying a total nobody consented to.
/// </remarks>
/// <param name="CheckoutSessionId">The session this came from, for tracing and for support.</param>
/// <param name="CartId">
/// The basket. It is also the reservation reference: stock is held against the cart before this
/// call, and Orders settles those holds when the order is confirmed or cancelled.
/// </param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="IdempotencyKey">The key the caller has already made unique. Echoed for tracing.</param>
/// <param name="Quote">The agreed, itemised price, grouped by seller.</param>
/// <param name="ShippingAddress">Where it goes.</param>
/// <param name="BillingAddress">Who it is billed to.</param>
/// <param name="Shipments">One plan per seller in the quote.</param>
/// <param name="PaymentMethod">Prepaid or cash on delivery.</param>
/// <param name="CouponCode">The code that was applied, if any, so redemption can be committed.</param>
/// <param name="Channel">Where the order came from: <c>web</c>, <c>app</c>, <c>admin</c>.</param>
public sealed record PlaceOrderRequest(
    Guid CheckoutSessionId,
    Guid CartId,
    Guid CustomerId,
    string IdempotencyKey,
    QuoteResult Quote,
    OrderAddress ShippingAddress,
    OrderAddress BillingAddress,
    IReadOnlyList<OrderShipmentPlan> Shipments,
    QuotePaymentMethod PaymentMethod,
    string? CouponCode,
    string Channel);

/// <summary>What the storefront needs to start a payment, or null when there is nothing to pay now.</summary>
/// <param name="Provider">The gateway, for example <c>razorpay</c>.</param>
/// <param name="ProviderOrderId">The gateway's own handle on the payment.</param>
/// <param name="PublicKey">The publishable key the checkout widget is initialised with.</param>
/// <param name="Amount">What the gateway is being asked for, after store credit.</param>
/// <param name="CurrencyCode">ISO 4217 code the amount is in.</param>
public sealed record PaymentInstruction(
    string Provider,
    string ProviderOrderId,
    string PublicKey,
    decimal Amount,
    string CurrencyCode);

/// <summary>The order that was created.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">The human-readable number a shopper quotes to support.</param>
/// <param name="Status">Where the order starts: awaiting payment, or already confirmed for COD.</param>
/// <param name="Payment">How to pay, or null for cash on delivery.</param>
public sealed record PlacedOrder(
    Guid OrderId,
    string OrderNumber,
    string Status,
    PaymentInstruction? Payment);

/// <summary>
/// Turns an agreed checkout into an order (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The seam between checkout and ordering, and it points this way round on purpose. Cart owns the
/// conversation with the shopper — what is in the basket, where it goes, how it is paid for — and
/// Orders owns the record of what was agreed. Inverting it, and having Orders read
/// <c>carts.checkout_sessions</c>, would put a join across a schema boundary on the one path in the
/// platform where money changes hands.
/// </para>
/// <para>
/// Declared at Step 13 and implemented at Step 14. Until then the Cart module registers an
/// implementation that refuses politely, so a place-order request against a half-built platform
/// gets a 503 that names the reason rather than a missing-service exception.
/// </para>
/// <para>
/// The caller has already made the request idempotent — one <c>checkout_placements</c> row wins the
/// unique index before this is called — so this method is invoked at most once per key.
/// </para>
/// </remarks>
public interface IOrderPlacement
{
    /// <summary>Creates the order, or explains why it could not be created.</summary>
    /// <param name="request">The agreed basket.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<PlacedOrder>> PlaceAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default);
}
