using KlaraHome.Contracts.IntegrationEvents;

namespace KlaraHome.Contracts.Catalog;

/// <summary>
/// A vendor's offer went live on the storefront (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// Inventory opens a stock item for it, Search indexes it, and Content may put it in a collection.
/// Published from the outbox in the transaction that activated the listing, so a consumer that
/// creates a stock row is reacting to something that certainly happened.
/// </remarks>
/// <param name="ListingId">The offer. This is the <c>listing_id</c> Inventory and Orders store.</param>
/// <param name="VendorId">The seller making the offer.</param>
/// <param name="VariantId">The sellable thing being offered.</param>
/// <param name="ProductId">The product the variant belongs to.</param>
/// <param name="Sku">The variant's stock-keeping unit.</param>
/// <param name="SellingPrice">The offer price, in the store currency.</param>
public sealed record ListingPublished(
    Guid ListingId,
    Guid VendorId,
    Guid VariantId,
    Guid ProductId,
    string Sku,
    decimal SellingPrice) : IntegrationEvent;

/// <summary>
/// A live offer's commercial terms changed.
/// </summary>
/// <remarks>
/// Raised only for a listing that is already <c>Active</c>: a draft being edited is not news, and
/// an index that reacted to every keystroke would be rebuilt for offers nobody can buy.
/// </remarks>
/// <param name="ListingId">The offer.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="VariantId">The sellable thing.</param>
/// <param name="SellingPrice">The new offer price.</param>
/// <param name="Mrp">The maximum retail price, which the offer price may never exceed.</param>
public sealed record ListingUpdated(
    Guid ListingId,
    Guid VendorId,
    Guid VariantId,
    decimal SellingPrice,
    decimal Mrp) : IntegrationEvent;

/// <summary>
/// An offer left the storefront - paused by its seller, archived, or withdrawn because the seller
/// stopped trading.
/// </summary>
/// <remarks>
/// Search drops it, and any buy box currently pointing at it has to be resolved again. It is not a
/// deletion: the listing still exists, and orders already placed against it are unaffected.
/// </remarks>
/// <param name="ListingId">The offer.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="VariantId">The sellable thing.</param>
/// <param name="Reason">Why it was withdrawn, in words an operator wrote or the system chose.</param>
public sealed record ListingDeactivated(
    Guid ListingId,
    Guid VendorId,
    Guid VariantId,
    string? Reason) : IntegrationEvent;
