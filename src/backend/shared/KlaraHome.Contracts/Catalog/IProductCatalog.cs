namespace KlaraHome.Contracts.Catalog;

/// <summary>
/// Everything another module is allowed to know about one vendor's offer.
/// </summary>
/// <remarks>
/// Deliberately the union of what Inventory, Cart, Orders and Shipping each need and nothing more:
/// what it is, who sells it, what it weighs, what it may be charged at, and the tax facts that must
/// be frozen onto an order line. A module that wants the description or the gallery is asking a
/// question the storefront endpoints answer.
/// </remarks>
/// <param name="ListingId">The offer.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="VariantId">The sellable thing.</param>
/// <param name="ProductId">The product it belongs to.</param>
/// <param name="CategoryId">
/// The product's category. Added for Pricing (docs/03-database-design.md §4.6), whose promotions
/// are scoped to categories and cannot read the catalogue's tables to find out which.
/// </param>
/// <param name="CategoryPath">
/// The category's materialised path, <c>/id/id/</c>, ancestors first. It is what makes a promotion
/// scoped to a parent category apply to everything beneath it without a second query - the
/// alternative is Pricing asking Catalog to walk the tree on every cart render.
/// </param>
/// <param name="BrandId">The product's brand, or null. Promotions are scoped to brands too.</param>
/// <param name="Sku">The stock-keeping unit.</param>
/// <param name="Name">The variant's full display name, product name included.</param>
/// <param name="IsPurchasable">Whether the offer, its variant, its product and its seller are all live.</param>
/// <param name="Mrp">Maximum retail price. Statutory in India and never below the selling price.</param>
/// <param name="SellingPrice">What the seller is asking, inclusive of GST.</param>
/// <param name="HsnCode">The HSN code the GST rate is resolved from.</param>
/// <param name="GstRate">The GST percentage, e.g. <c>18.0000</c>.</param>
/// <param name="WeightGrams">Dead weight, for a shipping rate.</param>
/// <param name="IsCodAllowed">Whether this seller accepts cash on delivery for this offer.</param>
/// <param name="MaxOrderQuantity">The most units one order may take, or null for no cap of its own.</param>
/// <param name="HandlingTimeHours">How long this seller needs before handing the parcel over.</param>
/// <param name="IsReturnable">Whether the product may be returned at all.</param>
/// <param name="ReturnWindowDays">The product's own return window, or null to use the store's.</param>
/// <param name="PrimaryImageFileId">The image a cart line or an order line renders.</param>
public sealed record ListingSummary(
    Guid ListingId,
    Guid VendorId,
    Guid VariantId,
    Guid ProductId,
    Guid CategoryId,
    string CategoryPath,
    Guid? BrandId,
    string Sku,
    string Name,
    bool IsPurchasable,
    decimal Mrp,
    decimal SellingPrice,
    string? HsnCode,
    decimal GstRate,
    int WeightGrams,
    bool IsCodAllowed,
    int? MaxOrderQuantity,
    int HandlingTimeHours,
    bool IsReturnable,
    int? ReturnWindowDays,
    Guid? PrimaryImageFileId);

/// <summary>
/// Reads offers from outside the Catalog module (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// Inventory keys its stock on <c>listing_id</c>, a cart line holds one, and an order line freezes a
/// snapshot of one. None of them may join to <c>catalog.listings</c>, so this is how those ids
/// become facts. It is the mirror of <c>IVendorDirectory</c>, and it exists for the same reason.
/// </para>
/// <para>
/// <see cref="ListingSummary.IsPurchasable"/> is the single question most callers actually have,
/// and it is answered here rather than reconstructed by each of them: it is true only when the
/// listing, its variant and its product are all active and the offer has not been soft-deleted.
/// Whether there is any <em>stock</em> is Inventory's question, deliberately not this one.
/// </para>
/// </remarks>
public interface IProductCatalog
{
    /// <summary>The published facts about one offer, or null when there is no such offer.</summary>
    /// <param name="listingId">The offer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<ListingSummary?> FindListingAsync(Guid listingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The published facts about several offers at once, keyed by id. Absent ids are simply not in
    /// the result - a cart holding a listing that has since been archived is a case the caller
    /// handles, not an error.
    /// </summary>
    /// <param name="listingIds">The offers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyDictionary<Guid, ListingSummary>> FindListingsAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken = default);
}
