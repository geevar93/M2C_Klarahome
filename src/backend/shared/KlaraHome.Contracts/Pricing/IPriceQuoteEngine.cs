namespace KlaraHome.Contracts.Pricing;

/// <summary>One line a caller wants priced.</summary>
/// <param name="LineId">
/// The caller's own identifier for the line — a cart line id, an order line id. Echoed back on the
/// quoted line so the caller can match them up without relying on ordering.
/// </param>
/// <param name="ListingId">The offer.</param>
/// <param name="Quantity">How many units. Must be positive.</param>
public sealed record QuoteLineRequest(Guid LineId, Guid ListingId, int Quantity);

/// <summary>How a basket is being paid for, because some promotions and fees depend on it.</summary>
public enum QuotePaymentMethod
{
    /// <summary>Paid before dispatch, through the gateway.</summary>
    Prepaid = 0,

    /// <summary>Cash on delivery. May attract a handling fee, and is excluded by some promotions.</summary>
    CashOnDelivery = 1,
}

/// <summary>
/// Everything the engine needs to price a basket. Nothing is read from ambient state: the same
/// request quotes the same way from a cart, a checkout, an order and an invoice reprint.
/// </summary>
/// <param name="Lines">The lines to price.</param>
/// <param name="CustomerId">
/// The shopper, when known. Decides per-customer usage limits, the first-order condition, and
/// whose wallet may be redeemed. Null for an anonymous quote, which simply sees fewer promotions.
/// </param>
/// <param name="PlaceOfSupplyStateId">
/// The <c>platform.states</c> row of the shipping address. It decides CGST+SGST versus IGST
/// (docs/02-domain-model.md §7). Null quotes at the store's own state, which is what a product
/// page does before an address exists.
/// </param>
/// <param name="CouponCode">A code the shopper typed, if any.</param>
/// <param name="PaymentMethod">How the basket is being paid for.</param>
/// <param name="IsFirstOrder">
/// Whether this would be the shopper's first order, for a first-order campaign. Supplied by the
/// caller rather than looked up: order history belongs to the Orders module, and Pricing asking it
/// on every cart render would be a query across a schema boundary on the hottest path in the
/// basket. False is the safe default - a first-order offer that fails to apply is a support
/// question, one that applies to a returning customer is money.
/// </param>
/// <param name="ShippingAmount">
/// Shipping charged for the whole basket, inclusive of tax, as Shipping computed it. Zero until
/// Step 16 exists; the engine still splits its tax and allocates it per seller.
/// </param>
/// <param name="WalletRedeemRequested">
/// How much store credit the shopper asked to apply. The engine clamps it to their balance and to
/// what is still payable, and never redeems anything by itself.
/// </param>
/// <param name="QuotedAt">
/// The instant the quote is for. Supplied so re-quoting an old order reproduces the tax rate and
/// the promotion window that were in force then, rather than today's.
/// </param>
public sealed record QuoteRequest(
    IReadOnlyList<QuoteLineRequest> Lines,
    Guid? CustomerId = null,
    Guid? PlaceOfSupplyStateId = null,
    string? CouponCode = null,
    QuotePaymentMethod PaymentMethod = QuotePaymentMethod.Prepaid,
    bool IsFirstOrder = false,
    decimal ShippingAmount = 0m,
    decimal WalletRedeemRequested = 0m,
    DateTimeOffset? QuotedAt = null);

/// <summary>
/// One line of a quote, itemised the way an Indian tax invoice has to be
/// (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// Every figure here is derivable from the ones above it, and they are all returned rather than
/// left to the caller: an order line, an invoice and a settlement each recompute the same number
/// otherwise, and the first time one of them rounds differently the books stop balancing.
/// </remarks>
/// <param name="LineId">The caller's identifier for the line.</param>
/// <param name="ListingId">The offer.</param>
/// <param name="VendorId">The seller, so a caller can group into sub-orders without asking Catalog.</param>
/// <param name="Sku">The stock-keeping unit, frozen onto the line.</param>
/// <param name="Name">The offer's display name, frozen onto the line.</param>
/// <param name="Quantity">Units.</param>
/// <param name="UnitMrp">Maximum retail price per unit.</param>
/// <param name="UnitPrice">Selling price per unit, inclusive of GST, before any discount.</param>
/// <param name="Gross">Unit price multiplied by quantity.</param>
/// <param name="LineDiscount">Discount from promotions that applied to this line specifically.</param>
/// <param name="OrderDiscountAllocated">This line's pro-rata share of an order-level discount.</param>
/// <param name="TaxableValue">The gross less both discounts, with the tax taken back out of it.</param>
/// <param name="HsnCode">The HSN the rate was resolved from.</param>
/// <param name="GstRate">The GST percentage applied.</param>
/// <param name="CessRate">The cess percentage applied.</param>
/// <param name="Cgst">Central GST. Zero on an inter-state supply.</param>
/// <param name="Sgst">State GST. Zero on an inter-state supply.</param>
/// <param name="Igst">Integrated GST. Zero on an intra-state supply.</param>
/// <param name="Cess">Compensation cess.</param>
/// <param name="LineTotal">What the shopper pays for this line: gross less both discounts.</param>
/// <param name="AppliedPromotions">The promotions that touched this line, in the order they applied.</param>
public sealed record QuoteLine(
    Guid LineId,
    Guid ListingId,
    Guid VendorId,
    string Sku,
    string Name,
    int Quantity,
    decimal UnitMrp,
    decimal UnitPrice,
    decimal Gross,
    decimal LineDiscount,
    decimal OrderDiscountAllocated,
    decimal TaxableValue,
    string? HsnCode,
    decimal GstRate,
    decimal CessRate,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    decimal Cess,
    decimal LineTotal,
    IReadOnlyList<Guid> AppliedPromotions);

/// <summary>One seller's share of a basket, which is the sub-order Orders will create.</summary>
/// <param name="VendorId">The seller.</param>
/// <param name="LineIds">The caller's line identifiers that belong to them.</param>
/// <param name="Subtotal">Their lines' gross.</param>
/// <param name="Discount">Their lines' discount, line-level and allocated.</param>
/// <param name="TaxableValue">Their lines' taxable value.</param>
/// <param name="TaxTotal">Their lines' CGST + SGST + IGST + cess.</param>
/// <param name="Shipping">Their share of shipping, inclusive of its tax.</param>
/// <param name="ShippingTax">The tax inside the shipping figure.</param>
/// <param name="Total">What the shopper pays for this seller's part.</param>
public sealed record QuoteVendorGroup(
    Guid VendorId,
    IReadOnlyList<Guid> LineIds,
    decimal Subtotal,
    decimal Discount,
    decimal TaxableValue,
    decimal TaxTotal,
    decimal Shipping,
    decimal ShippingTax,
    decimal Total);

/// <summary>A promotion the engine considered, and what it decided.</summary>
/// <param name="PromotionId">The promotion.</param>
/// <param name="Code">Its coupon code, or null for an automatic rule.</param>
/// <param name="Name">Its name, for the cart's "you saved" line.</param>
/// <param name="Type">Its type, verbatim from the promotion.</param>
/// <param name="Applied">Whether it actually reduced anything.</param>
/// <param name="DiscountAmount">How much it took off. Zero when it did not apply.</param>
/// <param name="Reason">
/// Why it did not apply, or null when it did. This is what a cart shows a shopper who typed a code
/// that turned out to be for somebody else's first order.
/// </param>
public sealed record QuotePromotion(
    Guid PromotionId,
    string? Code,
    string Name,
    string Type,
    bool Applied,
    decimal DiscountAmount,
    string? Reason);

/// <summary>
/// A fully itemised, auditable price for a basket. Snapshotted by Orders and re-rendered on the
/// invoice; nothing downstream recomputes any of it.
/// </summary>
/// <param name="Lines">The lines, itemised.</param>
/// <param name="VendorGroups">The same lines grouped by seller, which is how they will be ordered.</param>
/// <param name="Promotions">Every promotion considered, applied or not, with the reason.</param>
/// <param name="CurrencyCode">ISO 4217 code every amount is in.</param>
/// <param name="IsIntraState">
/// Whether the <em>store's own</em> supply - shipping and the COD fee - is intra-state. A line's
/// split is decided against its own seller's registration and may differ from this, which is why
/// the CGST/SGST/IGST figures are on the line rather than inferred from this flag.
/// </param>
/// <param name="PlaceOfSupplyStateCode">The GST state code every split was decided against.</param>
/// <param name="Subtotal">The lines' gross, before any discount.</param>
/// <param name="LineDiscountTotal">Discount attributed to specific lines.</param>
/// <param name="OrderDiscountTotal">Discount applied to the basket as a whole.</param>
/// <param name="DiscountTotal">The two above, together.</param>
/// <param name="TaxableValue">What the tax was computed on, after discount.</param>
/// <param name="CgstTotal">Central GST across the basket.</param>
/// <param name="SgstTotal">State GST across the basket.</param>
/// <param name="IgstTotal">Integrated GST across the basket.</param>
/// <param name="CessTotal">Compensation cess across the basket.</param>
/// <param name="TaxTotal">Every tax figure above, together.</param>
/// <param name="Shipping">Shipping charged, inclusive of its tax, after any free-shipping promotion.</param>
/// <param name="ShippingDiscount">Shipping taken off by a promotion.</param>
/// <param name="ShippingTax">The tax inside the shipping figure.</param>
/// <param name="CodFee">The cash-on-delivery handling fee, inclusive of its tax. Zero when prepaid.</param>
/// <param name="WalletApplied">Store credit actually redeemed. Never more than the balance or the amount due.</param>
/// <param name="RoundingAdjustment">
/// What was added or taken off to land the total on a whole rupee. Reported rather than hidden,
/// because it is a line on the invoice.
/// </param>
/// <param name="GrandTotal">What the shopper pays.</param>
/// <param name="AmountPayable">The grand total less store credit: what the gateway is asked for.</param>
/// <param name="CouponRejection">
/// Why the code the shopper typed did nothing, or null when it worked or none was typed. It is on
/// the quote rather than raised as an error because a basket with a bad coupon on it still has a
/// price, and refusing to quote one would leave the cart with nothing to render.
/// </param>
public sealed record QuoteResult(
    IReadOnlyList<QuoteLine> Lines,
    IReadOnlyList<QuoteVendorGroup> VendorGroups,
    IReadOnlyList<QuotePromotion> Promotions,
    string CurrencyCode,
    bool IsIntraState,
    string? PlaceOfSupplyStateCode,
    decimal Subtotal,
    decimal LineDiscountTotal,
    decimal OrderDiscountTotal,
    decimal DiscountTotal,
    decimal TaxableValue,
    decimal CgstTotal,
    decimal SgstTotal,
    decimal IgstTotal,
    decimal CessTotal,
    decimal TaxTotal,
    decimal Shipping,
    decimal ShippingDiscount,
    decimal ShippingTax,
    decimal CodFee,
    decimal WalletApplied,
    decimal RoundingAdjustment,
    decimal GrandTotal,
    decimal AmountPayable,
    string? CouponRejection);

/// <summary>
/// The one calculation engine (docs/01-architecture.md §2.1). Cart, checkout, orders and invoices
/// all call it; none of them computes a price, a discount or a tax figure of its own.
/// </summary>
/// <remarks>
/// <para>
/// That single-implementation rule is the whole point. A marketplace with two pieces of code that
/// both compute GST has two answers to what a customer owes, and the discrepancy surfaces at a tax
/// return rather than at a code review.
/// </para>
/// <para>
/// Quoting is a pure read: it evaluates promotions but does not redeem them, and it clamps a
/// wallet request but does not debit it. Committing either belongs to
/// <see cref="IPromotionLedger"/> and <see cref="IStoreCredit"/>, and it happens when the order is
/// placed rather than when it is priced.
/// </para>
/// </remarks>
public interface IPriceQuoteEngine
{
    /// <summary>Prices a basket, itemised.</summary>
    /// <param name="request">What to price.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<QuoteResult> QuoteAsync(QuoteRequest request, CancellationToken cancellationToken = default);
}
