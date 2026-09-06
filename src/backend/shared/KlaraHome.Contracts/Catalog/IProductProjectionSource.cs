namespace KlaraHome.Contracts.Catalog;

/// <summary>
/// One described property of a product, as a read-model needs it.
/// </summary>
/// <remarks>
/// Both halves are carried because a facet needs both and neither can be derived from the other: a
/// query filters on <see cref="Value"/> — the stable machine value a URL holds — and a shopper reads
/// <see cref="ValueLabel"/>. Resolving the label at read time would mean a join across a schema
/// boundary on the most-requested page the storefront has.
/// </remarks>
/// <param name="Code">The attribute's stable code, as <c>color</c>. This is the facet key.</param>
/// <param name="Name">The attribute's shopper-facing name, as <c>Colour</c>.</param>
/// <param name="Value">The machine value, as <c>beige</c>.</param>
/// <param name="ValueLabel">The value's shopper-facing label, as <c>Beige</c>.</param>
/// <param name="IsFilterable">Whether the catalogue offers this attribute as a filter.</param>
/// <param name="IsSearchable">Whether its value should feed the free-text index.</param>
public sealed record ProductAttributeProjection(
    string Code,
    string Name,
    string Value,
    string ValueLabel,
    bool IsFilterable,
    bool IsSearchable);

/// <summary>
/// Everything a read-model outside the Catalog module needs to describe one seller's offer.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately wider than <see cref="ListingSummary"/> and for a different job. That contract is
/// the union of what a cart, an order and a shipment need — identity, money and tax. This one is
/// what a <em>projection</em> needs: the words a shopper searches by, the names a facet is labelled
/// with, and the picture a result card renders. A module that wants to price a line asks the
/// narrower contract; nothing should ask this one for anything but a projection.
/// </para>
/// <para>
/// <see cref="IsBuyBox"/> is the reason this is not simply "a listing with more columns". A
/// marketplace shows one offer per variant, and which one that is comes out of a rule the operator
/// configures and the Catalog module owns. Answering it here means the search results and the
/// product page can never disagree about which seller won — and a consumer that resolved its own
/// winner would eventually disagree with the page it links to.
/// </para>
/// </remarks>
/// <param name="ListingId">The offer.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="VendorName">The seller's public name.</param>
/// <param name="VendorSlug">Their storefront path segment.</param>
/// <param name="VendorRating">Their average review score, or null when they have none.</param>
/// <param name="VariantId">The sellable thing being offered.</param>
/// <param name="ProductId">The product the variant belongs to.</param>
/// <param name="Sku">The stock-keeping unit.</param>
/// <param name="ProductName">The product's title.</param>
/// <param name="VariantName">The full display name, product name included.</param>
/// <param name="ProductSlug">The product's URL segment, so a result card can link to it.</param>
/// <param name="ShortDescription">The one-line summary, which is index-worthy text.</param>
/// <param name="CategoryId">The product's category.</param>
/// <param name="CategoryName">Its name, for a facet label.</param>
/// <param name="CategorySlug">Its URL segment.</param>
/// <param name="CategoryPath">Its materialised path, <c>/id/id/</c>, ancestors first.</param>
/// <param name="BrandId">The product's brand, or null.</param>
/// <param name="BrandName">The brand's name, for a facet label.</param>
/// <param name="BrandSlug">The brand's URL segment.</param>
/// <param name="IsPurchasable">Whether the offer, its variant, its product and its seller are all live.</param>
/// <param name="IsBuyBox">Whether this is the offer the storefront opens the variant on.</param>
/// <param name="Mrp">Maximum retail price. Statutory in India and never below the selling price.</param>
/// <param name="SellingPrice">The offer's own price, inclusive of GST, before any price list.</param>
/// <param name="CurrencyCode">ISO 4217 code both amounts are in.</param>
/// <param name="RatingAverage">The product's average review score, or null when it has none.</param>
/// <param name="RatingCount">How many reviews that is over.</param>
/// <param name="IsCodAllowed">Whether this seller accepts cash on delivery for this offer.</param>
/// <param name="IsReturnable">Whether the product may be returned at all.</param>
/// <param name="PrimaryImageFileId">The image a result card renders.</param>
/// <param name="PublishedAt">When the offer went live, for a "newest first" sort.</param>
/// <param name="Attributes">The product's described properties, for facets and for the index.</param>
public sealed record ProductProjection(
    Guid ListingId,
    Guid VendorId,
    string VendorName,
    string VendorSlug,
    decimal? VendorRating,
    Guid VariantId,
    Guid ProductId,
    string Sku,
    string ProductName,
    string VariantName,
    string ProductSlug,
    string? ShortDescription,
    Guid CategoryId,
    string CategoryName,
    string CategorySlug,
    string CategoryPath,
    Guid? BrandId,
    string? BrandName,
    string? BrandSlug,
    bool IsPurchasable,
    bool IsBuyBox,
    decimal Mrp,
    decimal SellingPrice,
    string CurrencyCode,
    decimal? RatingAverage,
    int RatingCount,
    bool IsCodAllowed,
    bool IsReturnable,
    Guid? PrimaryImageFileId,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ProductAttributeProjection> Attributes);

/// <summary>One page of a catalogue walk, and where to resume it.</summary>
/// <param name="VariantIds">
/// Every variant the page covered, including those that produced no offer at all. A consumer needs
/// these as well as the offers: a variant whose last listing was withdrawn returns nothing, and it
/// is precisely the one whose read-model row has to be retired.
/// </param>
/// <param name="Items">
/// Every live offer for the variants on this page. A variant's offers are never split across two
/// pages, so a consumer that groups by variant sees each group whole.
/// </param>
/// <param name="NextVariantCursor">
/// The last variant on this page, to be passed back as the cursor, or null when the walk is over.
/// </param>
public sealed record ProductProjectionPage(
    IReadOnlyList<Guid> VariantIds,
    IReadOnlyList<ProductProjection> Items,
    Guid? NextVariantCursor);

/// <summary>
/// Reads the catalogue in the shape a projection needs, from outside the Catalog module
/// (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// Added at Step 19 for the search index, which cannot join to <c>catalog.products</c> and must
/// nonetheless hold the product's name, its brand, its category path and its filterable attributes.
/// It is the mirror of <see cref="IProductCatalog"/> and exists for the same reason; the reporting
/// read-models at Step 21 are its second expected consumer.
/// </para>
/// <para>
/// Every method answers in whole variants. A projection keyed on the variant needs all of its offers
/// to know which one won, and a contract that returned one listing at a time would make the consumer
/// ask a second question it has no way to phrase.
/// </para>
/// </remarks>
public interface IProductProjectionSource
{
    /// <summary>
    /// Every live offer for the named variants, buy box marked. Absent variants are simply not in
    /// the result — a variant that has been archived is a case the caller handles by dropping its
    /// row, not an error.
    /// </summary>
    /// <param name="variantIds">The variants.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<ProductProjection>> FindByVariantsAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every live offer for every variant of the named products, buy box marked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added at Step 20 for merchandising. A curated collection holds product ids — that is what a
    /// person chooses and what docs/03-database-design.md §4.14 stores — while every read-model on
    /// this platform is keyed on the variant, so something has to translate between them. Doing it in
    /// the consumer would mean the CMS holding a second copy of the rule for which variant represents
    /// a product on a card, and the day the two disagreed a collection tile would quote a price the
    /// product page did not.
    /// </para>
    /// <para>
    /// Whole variants, like every other method here: a product's variants each arrive with all of
    /// their offers, so a caller that groups by variant sees each group complete and can apply the
    /// buy-box flag rather than guessing.
    /// </para>
    /// </remarks>
    /// <param name="productIds">The products.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<ProductProjection>> FindByProductsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every live offer for every variant of the product at one slug, buy box marked.
    /// </summary>
    /// <remarks>
    /// Added at Step 20 for structured data. A crawler asks for a URL and the storefront has to hand
    /// back a <c>Product</c> and an <c>Offer</c> graph for whatever is at it — and a URL carries a
    /// slug, not an id. The alternative was making the caller resolve the slug first, which would be
    /// a second contract and a second round trip for the same question.
    /// </remarks>
    /// <param name="productSlug">The product's URL segment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<ProductProjection>> FindBySlugAsync(
        string productSlug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Which variant each listing belongs to.
    /// </summary>
    /// <remarks>
    /// An integration event names a listing; a projection is keyed on a variant. This is the one
    /// translation between them, and it lives here rather than in the consumer because the mapping
    /// is the catalogue's fact. Listings the catalogue no longer has are simply absent.
    /// </remarks>
    /// <param name="listingIds">The offers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyDictionary<Guid, Guid>> FindVariantsOfAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks the whole catalogue, variant by variant, for a full rebuild.
    /// </summary>
    /// <param name="afterVariantId">Resume after this variant, or null to start at the beginning.</param>
    /// <param name="size">How many variants to return. The implementation may return fewer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<ProductProjectionPage> EnumerateAsync(
        Guid? afterVariantId,
        int size,
        CancellationToken cancellationToken = default);
}
