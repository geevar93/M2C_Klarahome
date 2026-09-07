using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Catalog.Domain;

/// <summary>
/// Where a product is in its life (docs/02-domain-model.md §5.3).
/// </summary>
/// <remarks>
/// The order of the values is the order of the happy path, and nothing depends on it: the legal
/// transitions are declared in <see cref="Product.IsTransitionAllowed"/>, not inferred from the
/// numbers, so inserting a state later cannot silently open a path.
/// </remarks>
internal enum ProductStatus
{
    /// <summary>Being written. Invisible to shoppers, and free of every publishing rule.</summary>
    Draft = 0,

    /// <summary>Submitted by a seller and waiting for the platform to look at it.</summary>
    PendingApproval = 1,

    /// <summary>Published. Its active variants may be offered and sold.</summary>
    Active = 2,

    /// <summary>Withdrawn from the storefront but not retired. Re-publishing needs no re-approval.</summary>
    Inactive = 3,

    /// <summary>Retired. Terminal — its history is kept because orders point at it.</summary>
    Archived = 4,
}

/// <summary>
/// The marketable concept — "Cotton Cushion Cover 40×40" (docs/02-domain-model.md §3).
/// </summary>
/// <remarks>
/// <para>
/// Not sellable by itself. A shopper buys a <see cref="Variant"/>, and pays whichever
/// <see cref="Listing"/> won the buy box for it. This row holds everything true of the concept
/// regardless of who sells it or which colour they picked: the description, the taxonomy, the
/// gallery, and the India compliance fields that the law attaches to the goods rather than to the
/// offer.
/// </para>
/// <para>
/// The compliance fields are on the product and not on the listing on purpose. HSN, GST rate and
/// country of origin are facts about the goods; two sellers offering the same cushion cover cannot
/// disagree about its HSN code, and letting them would produce two different tax treatments of one
/// purchase.
/// </para>
/// <para>
/// <see cref="IPlatformShared"/> is what makes the shared catalogue real rather than merely
/// intended. Vendor scoping is otherwise "your rows and nobody else's", the platform's included,
/// and under that rule a seller could never see the product the platform published — which is the
/// product several sellers are supposed to compete over. This table opts out of that half of the
/// rule; <c>CatalogScope.CanWrite</c> still refuses the write.
/// </para>
/// </remarks>
internal sealed class Product
    : AggregateRoot<Guid>, ITenantScoped, IAuditable, ISoftDeletable, IVendorScoped, IPlatformShared
{
    private Product(Guid id, string name, string slug, Guid categoryId)
        : base(id)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Slug = Guard.NotNullOrWhiteSpace(slug);
        CategoryId = Guard.NotEmpty(categoryId);
        Status = ProductStatus.Draft;
        Seo = new SeoMetadata();
        Manufacturer = new PartyDetails();
        Packer = new PartyDetails();
        Importer = new PartyDetails();
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Product()
    {
        Name = string.Empty;
        Slug = string.Empty;
        Seo = new SeoMetadata();
        Manufacturer = new PartyDetails();
        Packer = new PartyDetails();
        Importer = new PartyDetails();
    }

    /// <summary>The category it browses under.</summary>
    public Guid CategoryId { get; private set; }

    /// <summary>Its brand, or null for an unbranded good.</summary>
    public Guid? BrandId { get; private set; }

    /// <summary>The product title.</summary>
    public string Name { get; private set; }

    /// <summary>The URL path segment. Unique per tenant, because the PDP routes on it.</summary>
    public string Slug { get; private set; }

    /// <summary>The long description. HTML, sanitised on the way in.</summary>
    public string? Description { get; private set; }

    /// <summary>The one-line summary shown in a card and used as the default meta description.</summary>
    public string? ShortDescription { get; private set; }

    /// <summary>Where it is in its life.</summary>
    public ProductStatus Status { get; private set; }

    /// <summary>
    /// The seller who created it, or null for a product the platform owns.
    /// </summary>
    /// <remarks>
    /// This is what makes the product vendor-scoped: a seller who drafts their own product may edit
    /// it, and the global vendor filter keeps them out of everyone else's. A platform-owned product
    /// carries null and any seller may list against it, which is the shared-catalogue case a
    /// marketplace needs.
    /// </remarks>
    public Guid? VendorId { get; private set; }

    /// <summary>The HSN code the GST rate is resolved from. Mandatory before publishing.</summary>
    public string? HsnCode { get; private set; }

    /// <summary>The GST percentage, as <c>18.0000</c>. Mandatory before publishing.</summary>
    public decimal GstRate { get; private set; }

    /// <summary>ISO 3166-1 alpha-2 country of origin. Mandatory before publishing (Legal Metrology).</summary>
    public string? CountryOfOrigin { get; private set; }

    /// <summary>Who made it. Its name and address must be displayed.</summary>
    public PartyDetails Manufacturer { get; private set; }

    /// <summary>Who packed it, where that is a different company.</summary>
    public PartyDetails Packer { get; private set; }

    /// <summary>Who imported it. Mandatory for goods whose origin is not India.</summary>
    public PartyDetails Importer { get; private set; }

    /// <summary>Whether it may be returned at all.</summary>
    public bool IsReturnable { get; private set; } = true;

    /// <summary>Its own return window in days, or null to use the store's.</summary>
    public int? ReturnWindowDays { get; private set; }

    /// <summary>The warranty statement, printed on the PDP.</summary>
    public string? Warranty { get; private set; }

    /// <summary>
    /// The specification table, as ordered label/value pairs. Free-form, and deliberately separate
    /// from attributes: an attribute is queried, a specification is only read.
    /// </summary>
    public IReadOnlyList<Specification> Specifications
    {
        get => _specifications;
        private set => _specifications = [.. value];
    }

    private List<Specification> _specifications = [];

    /// <summary>What a crawler is told about the PDP.</summary>
    public SeoMetadata Seo { get; private set; }

    /// <summary>The average review score, written by the Reviews module. Null until it has reviews.</summary>
    public decimal? RatingAverage { get; private set; }

    /// <summary>How many reviews that average is over.</summary>
    public int RatingCount { get; private set; }

    /// <summary>When it first became <see cref="ProductStatus.Active"/>. Never reset.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

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

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <inheritdoc />
    public Guid? DeletedBy { get; private set; }

    /// <summary>Whether shoppers can currently see it.</summary>
    public bool IsPublished => Status == ProductStatus.Active;

    /// <summary>The country code that means the goods were made here, and need no importer.</summary>
    public const string India = "IN";

    /// <summary>Drafts a product.</summary>
    /// <param name="name">The title.</param>
    /// <param name="slug">The URL segment, already normalised and known to be free.</param>
    /// <param name="categoryId">The category it browses under.</param>
    /// <param name="vendorId">The seller who created it, or null for a platform-owned product.</param>
    public static Product Draft(string name, string slug, Guid categoryId, Guid? vendorId)
        => new(UuidV7.New(), name, slug, categoryId) { VendorId = vendorId };

    /// <summary>
    /// Whether the life cycle allows one status to follow another (docs/02-domain-model.md §5.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declared as a table rather than a chain of <c>if</c>s so that the whole machine can be read —
    /// and tested — in one place. Three edges are worth stating out loud.
    /// </para>
    /// <para>
    /// <see cref="ProductStatus.PendingApproval"/> may go back to <see cref="ProductStatus.Draft"/>,
    /// because "your images are too small" is the usual outcome of a real moderation queue and must
    /// not require the seller to start again. <see cref="ProductStatus.Inactive"/> may go straight
    /// back to <see cref="ProductStatus.Active"/> without re-approval, because unpublishing for a
    /// week is a merchandising decision and not a content change. And a
    /// <see cref="ProductStatus.Draft"/> may be published directly by platform staff, who are the
    /// approvers — sending their own draft to their own queue is theatre.
    /// </para>
    /// </remarks>
    /// <param name="from">The current status.</param>
    /// <param name="to">The status being moved to.</param>
    public static bool IsTransitionAllowed(ProductStatus from, ProductStatus to)
        => (from, to) switch
        {
            (ProductStatus.Draft, ProductStatus.PendingApproval) => true,
            (ProductStatus.Draft, ProductStatus.Active) => true,
            (ProductStatus.PendingApproval, ProductStatus.Draft) => true,
            (ProductStatus.PendingApproval, ProductStatus.Active) => true,
            (ProductStatus.Active, ProductStatus.Inactive) => true,
            (ProductStatus.Inactive, ProductStatus.Active) => true,
            (_, ProductStatus.Archived) => from != ProductStatus.Archived,
            _ => false,
        };

    /// <summary>
    /// Moves the product to a new status, or returns false if the life cycle does not allow it.
    /// </summary>
    /// <remarks>
    /// False rather than an exception: two moderators pressing "approve" on the same product is a
    /// conflict to report, not a programming error.
    /// </remarks>
    /// <param name="next">The status to move to.</param>
    /// <param name="at">When the transition happened.</param>
    public bool TransitionTo(ProductStatus next, DateTimeOffset at)
    {
        if (!IsTransitionAllowed(Status, next))
        {
            return false;
        }

        Status = next;

        // Set once, on first publication. A product unpublished for a season and brought back has
        // not been launched twice, and "new in" reads this.
        if (next == ProductStatus.Active)
        {
            PublishedAt ??= at;
        }

        return true;
    }

    /// <summary>
    /// Everything the law requires before this product may be sold, and what of it is missing
    /// (docs/02-domain-model.md §7.4).
    /// </summary>
    /// <remarks>
    /// Returned as a list of human sentences rather than a boolean, because the operator pressing
    /// "publish" has to be told what to go and fix. The variant-level half of the same rule lives
    /// on <see cref="Variant"/>, which owns MRP and net quantity.
    /// </remarks>
    public IReadOnlyList<string> ComplianceGaps()
    {
        var gaps = new List<string>();

        if (string.IsNullOrWhiteSpace(HsnCode))
        {
            gaps.Add("An HSN code is required before a product can be sold.");
        }

        if (GstRate < 0m)
        {
            gaps.Add("A GST rate is required before a product can be sold.");
        }

        if (string.IsNullOrWhiteSpace(CountryOfOrigin))
        {
            gaps.Add("A country of origin is required (Legal Metrology).");
        }

        if (Manufacturer.IsEmpty)
        {
            gaps.Add("The manufacturer's name and address must be declared and displayed.");
        }

        // An imported good has to name the importer; a domestic one has nobody to name. Asking for
        // an importer on an Indian-made cushion cover is how a compliance check gets switched off.
        if (!string.IsNullOrWhiteSpace(CountryOfOrigin)
            && !string.Equals(CountryOfOrigin, India, StringComparison.OrdinalIgnoreCase)
            && Importer.IsEmpty)
        {
            gaps.Add("Imported goods must declare the importer's name and address.");
        }

        return gaps;
    }

    /// <summary>Updates the marketing copy and taxonomy.</summary>
    /// <param name="name">The title.</param>
    /// <param name="slug">The URL segment.</param>
    /// <param name="categoryId">The category it browses under.</param>
    /// <param name="brandId">Its brand, or null.</param>
    /// <param name="shortDescription">The one-line summary.</param>
    /// <param name="description">The long description.</param>
    /// <param name="specifications">The specification table.</param>
    /// <param name="seo">Crawler metadata.</param>
    public void Describe(
        string name,
        string slug,
        Guid categoryId,
        Guid? brandId,
        string? shortDescription,
        string? description,
        IReadOnlyList<Specification> specifications,
        SeoMetadata seo)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Slug = Guard.NotNullOrWhiteSpace(slug);
        CategoryId = Guard.NotEmpty(categoryId);
        BrandId = brandId;
        ShortDescription = shortDescription;
        Description = description;
        Specifications = Guard.NotNull(specifications);
        Seo = Guard.NotNull(seo);
    }

    /// <summary>Records the India compliance fields.</summary>
    /// <param name="hsnCode">The HSN code.</param>
    /// <param name="gstRate">The GST percentage.</param>
    /// <param name="countryOfOrigin">ISO 3166-1 alpha-2 origin.</param>
    /// <param name="manufacturer">Who made it.</param>
    /// <param name="packer">Who packed it.</param>
    /// <param name="importer">Who imported it.</param>
    public void DeclareCompliance(
        string? hsnCode,
        decimal gstRate,
        string? countryOfOrigin,
        PartyDetails manufacturer,
        PartyDetails packer,
        PartyDetails importer)
    {
        HsnCode = string.IsNullOrWhiteSpace(hsnCode) ? null : hsnCode.Trim();
        GstRate = Guard.NotNegative(gstRate);
        CountryOfOrigin = string.IsNullOrWhiteSpace(countryOfOrigin)
            ? null
            : countryOfOrigin.Trim().ToUpperInvariant();
        Manufacturer = Guard.NotNull(manufacturer);
        Packer = Guard.NotNull(packer);
        Importer = Guard.NotNull(importer);
    }

    /// <summary>Records what the customer is promised after the sale.</summary>
    /// <param name="isReturnable">Whether it may be returned.</param>
    /// <param name="returnWindowDays">Its own window, or null to use the store's.</param>
    /// <param name="warranty">The warranty statement.</param>
    public void SetAfterSalesTerms(bool isReturnable, int? returnWindowDays, string? warranty)
    {
        IsReturnable = isReturnable;
        ReturnWindowDays = isReturnable && returnWindowDays is > 0 ? returnWindowDays : null;
        Warranty = warranty;
    }

    /// <summary>
    /// Writes the review projection. Owned by the Reviews module, which raises an event this module
    /// consumes (docs/02-domain-model.md §6).
    /// </summary>
    /// <param name="average">The average score.</param>
    /// <param name="count">How many reviews it is over.</param>
    public void ProjectRating(decimal? average, int count)
    {
        RatingAverage = average;
        RatingCount = Math.Max(count, 0);
    }

    /// <summary>Retires the product. History matters here, so it is never actually removed.</summary>
    /// <param name="at">When.</param>
    public void Delete(DateTimeOffset at)
    {
        DeletedAt = at;
        Status = ProductStatus.Archived;
    }
}

/// <summary>One row of the specification table.</summary>
/// <param name="Label">What the row is called — "Material".</param>
/// <param name="Value">What it says — "100% cotton".</param>
/// <param name="Group">An optional heading the row files under — "Fabric".</param>
internal sealed record Specification(string Label, string Value, string? Group = null);

/// <summary>
/// A product-level attribute value (docs/03-database-design.md §4.4).
/// </summary>
/// <remarks>
/// Four typed value columns, exactly one of which is populated, chosen by the attribute's data
/// type. A single text column would be simpler to write and impossible to facet on: "wattage
/// between 5 and 15" is a numeric predicate, and a cast in the <c>WHERE</c> clause defeats every
/// index and fails on the first row somebody typed a unit into.
/// </remarks>
internal sealed class ProductAttributeValue : Entity<Guid>, ITenantScoped
{
    private ProductAttributeValue(Guid id, Guid productId, Guid attributeId)
        : base(id)
    {
        ProductId = Guard.NotEmpty(productId);
        AttributeId = Guard.NotEmpty(attributeId);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ProductAttributeValue()
    {
    }

    /// <summary>The product.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>The attribute being given a value.</summary>
    public Guid AttributeId { get; private set; }

    /// <summary>The value, for a <c>text</c> attribute.</summary>
    public string? ValueText { get; private set; }

    /// <summary>The value, for a <c>number</c> attribute.</summary>
    public decimal? ValueNumber { get; private set; }

    /// <summary>The value, for a <c>boolean</c> attribute.</summary>
    public bool? ValueBoolean { get; private set; }

    /// <summary>The value, for a <c>date</c> attribute.</summary>
    public DateOnly? ValueDate { get; private set; }

    /// <summary>The chosen option, for a <c>select</c> or <c>multiselect</c> attribute.</summary>
    public Guid? ValueOptionId { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Records a value in whichever column the attribute's type calls for.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="attributeId">The attribute.</param>
    /// <param name="dataType">Its data type, which decides the column.</param>
    /// <param name="text">The raw value as text; parsed for the numeric, boolean and date types.</param>
    /// <param name="optionId">The chosen option, for the list types.</param>
    public static ProductAttributeValue Create(
        Guid productId,
        Guid attributeId,
        AttributeDataType dataType,
        string? text,
        Guid? optionId)
    {
        var value = new ProductAttributeValue(UuidV7.New(), productId, attributeId);
        value.Set(dataType, text, optionId);

        return value;
    }

    /// <summary>Overwrites the value, clearing every column the new type does not use.</summary>
    /// <param name="dataType">The attribute's data type.</param>
    /// <param name="text">The raw value as text.</param>
    /// <param name="optionId">The chosen option, for the list types.</param>
    public void Set(AttributeDataType dataType, string? text, Guid? optionId)
    {
        ValueText = null;
        ValueNumber = null;
        ValueBoolean = null;
        ValueDate = null;
        ValueOptionId = null;

        switch (dataType)
        {
            case AttributeDataType.Number:
                ValueNumber = decimal.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var number)
                    ? number
                    : null;
                break;

            case AttributeDataType.Boolean:
                ValueBoolean = bool.TryParse(text, out var flag) ? flag : null;
                break;

            case AttributeDataType.Date:
                ValueDate = DateOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var date)
                    ? date
                    : null;
                break;

            case AttributeDataType.Select:
            case AttributeDataType.MultiSelect:
                ValueOptionId = optionId;
                break;

            case AttributeDataType.Text:
            default:
                ValueText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                break;
        }
    }
}
