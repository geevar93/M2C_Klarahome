using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Search.Domain;

/// <summary>
/// One indexed variant: everything a result card renders and everything a filter matches on
/// (docs/03-database-design.md §4.13).
/// </summary>
/// <remarks>
/// <para>
/// One row per variant, carrying the offer that won its buy box. Not one row per listing: a
/// marketplace shows one offer per sellable thing, and a result set with four rows for the same
/// cushion — one per seller — is the failure mode this shape exists to prevent. Which offer won is
/// resolved by the Catalog module with the operator's configured rule, so the row a shopper clicks
/// and the page they land on can never name two different sellers.
/// </para>
/// <para>
/// Every column here is a copy of something another module owns. That is the point of a projection
/// and it is also its one risk: a copy nobody refreshes is a price that lies. Four integration
/// events keep it honest — a listing changing, a price changing, stock moving and an order being
/// confirmed — and the whole table can be rebuilt from the catalogue at any time, which is what
/// makes a wrong row an operational nuisance rather than a loss.
/// </para>
/// <para>
/// <c>search_vector</c> is a <b>generated</b> column rather than a value this class writes, and the
/// database keeps it in step with the four text columns it is built from. A trigger would have been
/// the alternative, and it would have needed to be right in three places — insert, update, and the
/// rebuild that bypasses both.
/// </para>
/// </remarks>
internal sealed class ProductSearchDocument : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    /// <summary>The longest name this table stores, matching the catalogue's own limit.</summary>
    public const int MaxNameLength = 300;

    /// <summary>The longest free-text blob fed to the index from attributes and the summary.</summary>
    public const int MaxKeywordsLength = 4000;

    private ProductSearchDocument(Guid id, Guid variantId)
        : base(id)
    {
        VariantId = variantId;
        Sku = string.Empty;
        ProductName = string.Empty;
        VariantName = string.Empty;
        ProductSlug = string.Empty;
        CategoryName = string.Empty;
        CategorySlug = string.Empty;
        CategoryPath = string.Empty;
        VendorName = string.Empty;
        VendorSlug = string.Empty;
        CurrencyCode = Money.Inr;
        Attributes = "{}";
        AttributeMeta = "{}";
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ProductSearchDocument()
    {
        Sku = string.Empty;
        ProductName = string.Empty;
        VariantName = string.Empty;
        ProductSlug = string.Empty;
        CategoryName = string.Empty;
        CategorySlug = string.Empty;
        CategoryPath = string.Empty;
        VendorName = string.Empty;
        VendorSlug = string.Empty;
        CurrencyCode = Money.Inr;
        Attributes = "{}";
        AttributeMeta = "{}";
    }

    /// <summary>The sellable thing this row indexes. Unique within a tenant.</summary>
    public Guid VariantId { get; private set; }

    /// <summary>The product it belongs to.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>The offer that won the buy box. This is what a cart line will point at.</summary>
    public Guid ListingId { get; private set; }

    /// <summary>The seller behind that offer.</summary>
    public Guid VendorId { get; private set; }

    /// <summary>The stock-keeping unit, indexed so a shopper can paste one in.</summary>
    public string Sku { get; private set; }

    /// <summary>The product's title. The highest-weighted text in the index.</summary>
    public string ProductName { get; private set; }

    /// <summary>The full display name, product name included.</summary>
    public string VariantName { get; private set; }

    /// <summary>The product's URL segment, so a result card can link without a second query.</summary>
    public string ProductSlug { get; private set; }

    /// <summary>The brand, or null.</summary>
    public Guid? BrandId { get; private set; }

    /// <summary>Its name, for the facet label and for the index.</summary>
    public string? BrandName { get; private set; }

    /// <summary>Its URL segment.</summary>
    public string? BrandSlug { get; private set; }

    /// <summary>The category the product browses under.</summary>
    public Guid CategoryId { get; private set; }

    /// <summary>Its name, for the facet label and for the index.</summary>
    public string CategoryName { get; private set; }

    /// <summary>Its URL segment.</summary>
    public string CategorySlug { get; private set; }

    /// <summary>
    /// The category's materialised path, <c>/id/id/</c>, ancestors first.
    /// </summary>
    /// <remarks>
    /// Kept as the catalogue wrote it, for the browse index docs/03-database-design.md §5 names and
    /// for anybody reading a row to work out where a product sits. Filtering uses
    /// <see cref="CategoryIds"/> instead, for the reason set out there.
    /// </remarks>
    public string CategoryPath { get; private set; }

    /// <summary>
    /// Every category this product sits under: its own, and each of its ancestors.
    /// </summary>
    /// <remarks>
    /// Parsed out of <see cref="CategoryPath"/> rather than carried separately, so the two cannot
    /// disagree. It exists because filtering by a parent category is otherwise a problem with no good
    /// answer: a prefix match on the path needs the caller to know the ancestors, and a substring
    /// match cannot use an index. An array with a GIN index on it turns "everything under Furniture"
    /// into one index lookup from the id the shopper clicked, and nothing else has to be known.
    /// </remarks>
    public List<Guid> CategoryIds { get; private set; } = [];

    /// <summary>The seller's public name, for the facet label.</summary>
    public string VendorName { get; private set; }

    /// <summary>Their storefront path segment.</summary>
    public string VendorSlug { get; private set; }

    /// <summary>Their average review score, or null when they have none.</summary>
    public decimal? VendorRating { get; private set; }

    /// <summary>
    /// The searchable text that has no column of its own: the summary and the searchable attributes.
    /// </summary>
    /// <remarks>
    /// The lowest-weighted input to the index. It exists so that "waterproof" finds the cushion
    /// whose fabric attribute says so, without that word outranking a product actually called
    /// Waterproof Cushion.
    /// </remarks>
    public string? Keywords { get; private set; }

    /// <summary>
    /// The filterable attributes, as <c>{"color": ["beige"], "size": ["m"]}</c>.
    /// </summary>
    /// <remarks>
    /// Serialised <c>jsonb</c> held as a string on the entity, because nothing in this module reads
    /// it back through EF — every query that touches it is raw SQL using the <c>?|</c> containment
    /// operator against a GIN index, and deserialising it into a dictionary on the way to a facet
    /// count would be pure waste.
    /// </remarks>
    public string Attributes { get; private set; }

    /// <summary>
    /// The display names behind those values, as
    /// <c>{"color": {"name": "Colour", "values": {"beige": "Beige"}}}</c>.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Attributes"/> so the filtering column stays a plain map of arrays
    /// that <c>?|</c> can match against. A single structure carrying both would have to be walked
    /// rather than indexed.
    /// </remarks>
    public string AttributeMeta { get; private set; }

    /// <summary>Maximum retail price. Statutory in India and always displayed.</summary>
    public decimal Mrp { get; private set; }

    /// <summary>What the winning offer costs, inclusive of GST, after the price list that applied.</summary>
    public decimal Price { get; private set; }

    /// <summary>ISO 4217 code both amounts are in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>How far below MRP that is, as a whole number, for the discount facet and sort.</summary>
    public int DiscountPercent { get; private set; }

    /// <summary>The product's average review score, or null when it has none.</summary>
    public decimal? RatingAverage { get; private set; }

    /// <summary>How many reviews that is over.</summary>
    public int RatingCount { get; private set; }

    /// <summary>Whether anything can be sold right now.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>How many units, for the storefront's "only 2 left" line.</summary>
    public int QuantityAvailable { get; private set; }

    /// <summary>Whether the winning seller accepts cash on delivery.</summary>
    public bool IsCodAllowed { get; private set; }

    /// <summary>Whether the product may be returned at all.</summary>
    public bool IsReturnable { get; private set; }

    /// <summary>How many sellers are offering this variant, for the "N offers from" line.</summary>
    public int OfferCount { get; private set; }

    /// <summary>Units sold, all time. The raw material of <see cref="PopularityScore"/>.</summary>
    public long UnitsSold { get; private set; }

    /// <summary>
    /// How popular this variant is, on a scale nothing else on the row shares.
    /// </summary>
    /// <remarks>
    /// Units sold, damped, so that one runaway product cannot flatten the rest of the ranking: the
    /// score enters the blend as <c>score / (1 + score)</c>, which is bounded above by one however
    /// many units were sold. A raw count would have made the ranking weights meaningless the first
    /// time something sold ten thousand.
    /// </remarks>
    public decimal PopularityScore { get; private set; }

    /// <summary>The image a result card renders.</summary>
    public Guid? PrimaryImageFileId { get; private set; }

    /// <summary>When the winning offer went live, for the newest-first sort.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>
    /// Whether this row may be returned at all.
    /// </summary>
    /// <remarks>
    /// False when every offer for the variant is paused, or its seller has stopped trading. The row
    /// is kept rather than deleted so that the popularity it has accumulated survives an offer being
    /// paused for a week — deleting and recreating it would silently reset the sales history that
    /// orders the whole catalogue.
    /// </remarks>
    public bool IsActive { get; private set; }

    /// <summary>When the row was last rebuilt from its sources.</summary>
    /// <remarks>
    /// The staleness signal the reindex sweep works from, and the answer to "is the index behind"
    /// that an operator otherwise has to guess at.
    /// </remarks>
    public DateTimeOffset IndexedAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Opens a row for a variant nothing has indexed yet.</summary>
    /// <param name="variantId">The sellable thing.</param>
    public static ProductSearchDocument Open(Guid variantId)
        => new(UuidV7.New(), Guard.NotEmpty(variantId));

    /// <summary>
    /// Replaces everything the catalogue owns: the offer that won, the words, the money and the
    /// pictures.
    /// </summary>
    /// <remarks>
    /// Deliberately a whole-row write rather than a set of property setters. The fields are copies
    /// of one snapshot and they are only consistent together; a partial update is how a row comes to
    /// carry one seller's price under another seller's name.
    /// </remarks>
    /// <param name="content">The snapshot to write.</param>
    /// <param name="indexedAt">When it was taken.</param>
    public void Describe(SearchDocumentContent content, DateTimeOffset indexedAt)
    {
        ArgumentNullException.ThrowIfNull(content);

        ProductId = content.ProductId;
        ListingId = content.ListingId;
        VendorId = content.VendorId;
        Sku = Guard.MaxLength(content.Sku, MaxNameLength);
        ProductName = Guard.MaxLength(content.ProductName, MaxNameLength);
        VariantName = Guard.MaxLength(content.VariantName, MaxNameLength);
        ProductSlug = content.ProductSlug;
        BrandId = content.BrandId;
        BrandName = content.BrandName;
        BrandSlug = content.BrandSlug;
        CategoryId = content.CategoryId;
        CategoryName = content.CategoryName;
        CategorySlug = content.CategorySlug;
        CategoryPath = content.CategoryPath;
        CategoryIds = AncestorsOf(content.CategoryPath);
        VendorName = content.VendorName;
        VendorSlug = content.VendorSlug;
        VendorRating = content.VendorRating;
        Keywords = content.Keywords is { Length: > MaxKeywordsLength }
            ? content.Keywords[..MaxKeywordsLength]
            : content.Keywords;
        Attributes = content.Attributes;
        AttributeMeta = content.AttributeMeta;
        Mrp = content.Mrp;
        Price = content.Price;
        CurrencyCode = content.CurrencyCode;
        DiscountPercent = DiscountOf(content.Mrp, content.Price);
        RatingAverage = content.RatingAverage;
        RatingCount = content.RatingCount;
        IsCodAllowed = content.IsCodAllowed;
        IsReturnable = content.IsReturnable;
        OfferCount = content.OfferCount;
        PrimaryImageFileId = content.PrimaryImageFileId;
        PublishedAt = content.PublishedAt;
        IsActive = content.IsActive;
        IndexedAt = indexedAt;
    }

    /// <summary>Records what Inventory says is on the shelf.</summary>
    /// <remarks>
    /// Separate from <see cref="Describe"/> because it arrives separately and far more often. Stock
    /// moves on every sale; a product's name does not.
    /// </remarks>
    /// <param name="quantityAvailable">Units that may still be sold.</param>
    /// <param name="isAvailable">Whether anything may be sold, backorders included.</param>
    /// <param name="indexedAt">When this was learnt.</param>
    public void RecordAvailability(int quantityAvailable, bool isAvailable, DateTimeOffset indexedAt)
    {
        QuantityAvailable = Math.Max(quantityAvailable, 0);
        IsAvailable = isAvailable;
        IndexedAt = indexedAt;
    }

    /// <summary>Records the price that now applies.</summary>
    /// <param name="price">What the winning offer costs, inclusive of GST.</param>
    /// <param name="currencyCode">ISO 4217 code it is in.</param>
    /// <param name="indexedAt">When this was learnt.</param>
    public void RecordPrice(decimal price, string currencyCode, DateTimeOffset indexedAt)
    {
        Price = Guard.NotNegative(price);
        CurrencyCode = Guard.NotNullOrWhiteSpace(currencyCode);
        DiscountPercent = DiscountOf(Mrp, price);
        IndexedAt = indexedAt;
    }

    /// <summary>Adds units sold, and recomputes the damped popularity score.</summary>
    /// <remarks>
    /// Additive rather than absolute, because the fact that arrives is "this order contained three
    /// of these" and not "this product has now sold 412". A rebuild does not reset it: sales history
    /// is the one thing on this row that no other module can answer, and losing it would reorder the
    /// entire catalogue.
    /// </remarks>
    /// <param name="units">How many units were sold. Ignored when not positive.</param>
    public void RecordSale(int units)
    {
        if (units <= 0)
        {
            return;
        }

        UnitsSold += units;
        PopularityScore = PopularityOf(UnitsSold);
    }

    /// <summary>Takes the row out of results without losing what it knows.</summary>
    /// <param name="indexedAt">When it was withdrawn.</param>
    public void Deactivate(DateTimeOffset indexedAt)
    {
        IsActive = false;
        IsAvailable = false;
        IndexedAt = indexedAt;
    }

    /// <summary>
    /// The category ids in a materialised path, <c>/a/b/c/</c>, ancestors first.
    /// </summary>
    /// <remarks>
    /// Segments that are not identifiers are skipped rather than throwing. A malformed path is a
    /// catalogue defect, and the right answer to one is a product that is harder to filter to — not
    /// an index rebuild that stops.
    /// </remarks>
    /// <param name="path">The materialised path.</param>
    public static List<Guid> AncestorsOf(string? path)
    {
        var ids = new List<Guid>();

        if (string.IsNullOrWhiteSpace(path))
        {
            return ids;
        }

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(segment, out var id) && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>The saving as a whole-number percentage of MRP, for a "20% off" badge.</summary>
    /// <remarks>
    /// The same arithmetic as <c>EffectivePrice.DiscountPercent</c> in the Pricing contract, and
    /// deliberately so: a badge that disagreed between a search result and a product page is the
    /// kind of defect nobody reports and everybody notices.
    /// </remarks>
    /// <param name="mrp">The maximum retail price.</param>
    /// <param name="price">What is being asked.</param>
    public static int DiscountOf(decimal mrp, decimal price)
        => mrp <= 0m || price >= mrp
            ? 0
            : (int)Math.Round((mrp - price) / mrp * 100m, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The damped popularity of a variant that has sold <paramref name="unitsSold"/> units.
    /// </summary>
    /// <remarks>
    /// The natural logarithm of one plus the count. Sales are distributed by a power law — a
    /// marketplace's best seller outsells its median by three or four orders of magnitude — and a
    /// raw count as a ranking term would mean nothing but that one product ever appeared first. The
    /// log turns "ten thousand times as many" into "three times as much", which is roughly what a
    /// shopper means by more popular.
    /// </remarks>
    /// <param name="unitsSold">Units sold, all time.</param>
    public static decimal PopularityOf(long unitsSold)
        => unitsSold <= 0
            ? 0m
            : Math.Round((decimal)Math.Log(1d + unitsSold), 4, MidpointRounding.ToEven);
}

/// <summary>
/// The snapshot of a variant that <see cref="ProductSearchDocument.Describe"/> writes.
/// </summary>
/// <remarks>
/// A parameter object rather than twenty-five arguments, and a record so a builder can assemble it
/// from three modules' answers and hand it over once. Everything on it is a copy of a fact another
/// module owns.
/// </remarks>
/// <param name="ProductId">The product.</param>
/// <param name="ListingId">The offer that won the buy box.</param>
/// <param name="VendorId">Its seller.</param>
/// <param name="Sku">The stock-keeping unit.</param>
/// <param name="ProductName">The product's title.</param>
/// <param name="VariantName">The full display name.</param>
/// <param name="ProductSlug">The product's URL segment.</param>
/// <param name="BrandId">The brand, or null.</param>
/// <param name="BrandName">Its name.</param>
/// <param name="BrandSlug">Its URL segment.</param>
/// <param name="CategoryId">The category.</param>
/// <param name="CategoryName">Its name.</param>
/// <param name="CategorySlug">Its URL segment.</param>
/// <param name="CategoryPath">Its materialised path.</param>
/// <param name="VendorName">The seller's public name.</param>
/// <param name="VendorSlug">Their storefront path segment.</param>
/// <param name="VendorRating">Their average review score.</param>
/// <param name="Keywords">The low-weight searchable text.</param>
/// <param name="Attributes">The filterable attributes as a <c>jsonb</c> document.</param>
/// <param name="AttributeMeta">Their display names as a <c>jsonb</c> document.</param>
/// <param name="Mrp">Maximum retail price.</param>
/// <param name="Price">What the winning offer costs.</param>
/// <param name="CurrencyCode">ISO 4217 code.</param>
/// <param name="RatingAverage">The product's average review score.</param>
/// <param name="RatingCount">How many reviews.</param>
/// <param name="IsCodAllowed">Whether cash on delivery is accepted.</param>
/// <param name="IsReturnable">Whether the product may be returned.</param>
/// <param name="OfferCount">How many sellers offer the variant.</param>
/// <param name="PrimaryImageFileId">The image a result card renders.</param>
/// <param name="PublishedAt">When the winning offer went live.</param>
/// <param name="IsActive">Whether the row may be returned in results.</param>
internal sealed record SearchDocumentContent(
    Guid ProductId,
    Guid ListingId,
    Guid VendorId,
    string Sku,
    string ProductName,
    string VariantName,
    string ProductSlug,
    Guid? BrandId,
    string? BrandName,
    string? BrandSlug,
    Guid CategoryId,
    string CategoryName,
    string CategorySlug,
    string CategoryPath,
    string VendorName,
    string VendorSlug,
    decimal? VendorRating,
    string? Keywords,
    string Attributes,
    string AttributeMeta,
    decimal Mrp,
    decimal Price,
    string CurrencyCode,
    decimal? RatingAverage,
    int RatingCount,
    bool IsCodAllowed,
    bool IsReturnable,
    int OfferCount,
    Guid? PrimaryImageFileId,
    DateTimeOffset? PublishedAt,
    bool IsActive);
