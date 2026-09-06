using KlaraHome.Contracts.Pricing;

namespace KlaraHome.Modules.Carts.Application.Carts;

/// <summary>
/// Something the shopper has to know about a line or a basket, in words they can act on.
/// </summary>
/// <remarks>
/// The whole point of cart validation is that "you cannot check out" is useless on its own. Each
/// issue carries a stable code the storefront switches on, a sentence a person can read, and
/// whether it stops the order — because a price that went down is worth mentioning and is not worth
/// blocking a sale over.
/// </remarks>
/// <param name="Code">The stable code, for example <c>CART_PRICE_CHANGED</c>.</param>
/// <param name="Message">What to tell the shopper.</param>
/// <param name="IsBlocking">Whether checkout is refused until it is resolved.</param>
internal sealed record CartIssue(string Code, string Message, bool IsBlocking);

/// <summary>The codes <see cref="CartIssue"/> uses, so the API and the storefront cannot spell them differently.</summary>
internal static class CartIssueCodes
{
    /// <summary>The offer has been withdrawn, archived or is no longer purchasable.</summary>
    public const string ListingUnavailable = "CART_LISTING_UNAVAILABLE";

    /// <summary>The seller has been suspended or has left the marketplace.</summary>
    public const string VendorInactive = "CART_VENDOR_INACTIVE";

    /// <summary>There is not enough stock for the quantity in the basket.</summary>
    public const string OutOfStock = "CART_ITEM_OUT_OF_STOCK";

    /// <summary>The quantity was reduced to what may actually be bought.</summary>
    public const string QuantityReduced = "CART_QUANTITY_REDUCED";

    /// <summary>The price has moved since the shopper last saw it.</summary>
    public const string PriceChanged = "CART_PRICE_CHANGED";

    /// <summary>The seller does not deliver to the chosen address.</summary>
    public const string NotServiceable = "CART_NOT_SERVICEABLE";

    /// <summary>The coupon the shopper typed did nothing, and why.</summary>
    public const string CouponRejected = "CART_COUPON_REJECTED";
}

/// <summary>One offer in a basket, as the API states it.</summary>
/// <param name="Id">The line.</param>
/// <param name="ListingId">The offer.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="VendorName">What the seller is called, so the group has a heading.</param>
/// <param name="Sku">The stock-keeping unit.</param>
/// <param name="Name">The offer's display name.</param>
/// <param name="ImageFileId">The image the line renders.</param>
/// <param name="Quantity">How many units.</param>
/// <param name="SavedForLater">Whether the shopper set it aside.</param>
/// <param name="UnitMrp">Maximum retail price per unit, for the struck-through figure.</param>
/// <param name="UnitPrice">What a unit costs now, inclusive of GST.</param>
/// <param name="UnitPriceWhenAdded">What it cost when the shopper last saw a price for it.</param>
/// <param name="LineTotal">What the shopper pays for this line.</param>
/// <param name="QuantityAvailable">What may still be sold, so the stepper can cap itself.</param>
/// <param name="IsCodAllowed">Whether this offer may be paid for at the door.</param>
/// <param name="Issues">Anything the shopper needs to know about this line.</param>
internal sealed record CartLineResponse(
    Guid Id,
    Guid ListingId,
    Guid VendorId,
    string VendorName,
    string Sku,
    string Name,
    Guid? ImageFileId,
    int Quantity,
    bool SavedForLater,
    decimal UnitMrp,
    decimal UnitPrice,
    decimal UnitPriceWhenAdded,
    decimal LineTotal,
    int QuantityAvailable,
    bool IsCodAllowed,
    IReadOnlyList<CartIssue> Issues);

/// <summary>
/// One seller's share of a basket, which is the sub-order it will become.
/// </summary>
/// <remarks>
/// Grouping is not a presentation choice. A multi-vendor basket is several parcels with several
/// dispatch promises and several invoices, and a storefront that renders it as one list is
/// promising a delivery date the platform cannot keep.
/// </remarks>
/// <param name="VendorId">The seller.</param>
/// <param name="VendorName">What they are called.</param>
/// <param name="DispatchSlaHours">How long they have to hand the parcel over.</param>
/// <param name="LineIds">Their lines.</param>
/// <param name="Subtotal">Their lines' gross.</param>
/// <param name="Discount">Their lines' discount, line-level and allocated.</param>
/// <param name="TaxTotal">Their lines' GST and cess.</param>
/// <param name="Shipping">Their share of delivery, inclusive of its tax. Zero until one is chosen.</param>
/// <param name="Total">What the shopper pays for this seller's part.</param>
internal sealed record CartVendorGroupResponse(
    Guid VendorId,
    string VendorName,
    int DispatchSlaHours,
    IReadOnlyList<Guid> LineIds,
    decimal Subtotal,
    decimal Discount,
    decimal TaxTotal,
    decimal Shipping,
    decimal Total);

/// <summary>A basket, as the API states it.</summary>
/// <param name="Id">The basket.</param>
/// <param name="CustomerId">The shopper, or null while it belongs only to a browser.</param>
/// <param name="Status">Where it is in its life.</param>
/// <param name="CurrencyCode">ISO 4217 code every figure is in.</param>
/// <param name="CouponCode">The code the shopper typed, if any.</param>
/// <param name="LineCount">How many lines are in it, excluding those saved for later.</param>
/// <param name="ExpiresAt">When it lapses if nobody comes back.</param>
/// <param name="Lines">The lines, in the order they were added.</param>
/// <param name="SavedForLater">The lines the shopper set aside. Not priced and not checked out.</param>
/// <param name="Groups">The same lines grouped by seller.</param>
/// <param name="Quote">The itemised price, or null for an empty basket.</param>
/// <param name="Issues">Anything that applies to the basket as a whole.</param>
/// <param name="IsReadyForCheckout">Whether anything is blocking payment.</param>
internal sealed record CartResponse(
    Guid Id,
    Guid? CustomerId,
    string Status,
    string CurrencyCode,
    string? CouponCode,
    int LineCount,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<CartLineResponse> Lines,
    IReadOnlyList<CartLineResponse> SavedForLater,
    IReadOnlyList<CartVendorGroupResponse> Groups,
    QuoteResult? Quote,
    IReadOnlyList<CartIssue> Issues,
    bool IsReadyForCheckout);
