using KlaraHome.Contracts.IntegrationEvents;

namespace KlaraHome.Contracts.Pricing;

/// <summary>
/// The selling price of an offer changed (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Search reprojects its price facet and its sort from it, and Reviews uses it to fire the
/// price-drop alerts a shopper subscribed to. Both of those need the old figure as well as the new
/// one — "it dropped" is not a statement either could make from a single number without keeping
/// state of its own.
/// </para>
/// <para>
/// Published from the outbox in the transaction that wrote the price list item, so a consumer
/// reacting to it is reacting to a price that certainly took effect. A scheduled list that opens
/// or closes on its window raises it too, because a price that changed because the clock moved is
/// still a price that changed.
/// </para>
/// </remarks>
/// <param name="ListingId">The offer whose price moved.</param>
/// <param name="VendorId">The seller, so a consumer can scope without asking Catalog.</param>
/// <param name="PreviousPrice">What it was, or null when the offer had no list price before.</param>
/// <param name="NewPrice">What it is now, inclusive of GST.</param>
/// <param name="CurrencyCode">ISO 4217 code both amounts are in.</param>
/// <param name="PriceListId">The list that changed, or null when the offer fell back to its own price.</param>
public sealed record PriceChanged(
    Guid ListingId,
    Guid VendorId,
    decimal? PreviousPrice,
    decimal NewPrice,
    string CurrencyCode,
    Guid? PriceListId) : IntegrationEvent
{
    /// <summary>Whether this was a drop, which is the only kind an alert subscriber asked about.</summary>
    public bool IsDrop => PreviousPrice is { } previous && NewPrice < previous;
}

/// <summary>
/// A promotion was redeemed against an order.
/// </summary>
/// <remarks>
/// Reporting counts campaign performance from it and Notifications can thank a shopper for using a
/// code. It is raised by the ledger rather than by the quote engine: a promotion a cart merely
/// displayed has not been used, and counting those would make every campaign look successful.
/// </remarks>
/// <param name="PromotionId">The promotion.</param>
/// <param name="Code">Its coupon code, or null for an automatic rule.</param>
/// <param name="OrderId">The order it was used on.</param>
/// <param name="CustomerId">The shopper, when the order had one.</param>
/// <param name="DiscountAmount">What it took off.</param>
public sealed record PromotionRedeemed(
    Guid PromotionId,
    string? Code,
    Guid OrderId,
    Guid? CustomerId,
    decimal DiscountAmount) : IntegrationEvent;
