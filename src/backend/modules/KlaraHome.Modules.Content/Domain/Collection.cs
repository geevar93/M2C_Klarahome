using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Content.Domain;

/// <summary>How a collection decides what is in it (docs/03-database-design.md §4.14).</summary>
internal enum CollectionKind
{
    /// <summary>A merchandiser chose every product, one at a time.</summary>
    Manual = 0,

    /// <summary>
    /// A merchandiser wrote a rule and the platform keeps the membership current.
    /// </summary>
    /// <remarks>
    /// Still materialised into <c>collection_items</c> rather than evaluated per request. The rule is
    /// the input; the rows are the answer, refreshed when the catalogue changes and by a sweep. A
    /// collection evaluated live would mean the storefront reading the whole catalogue on every home
    /// page render, across a schema boundary it is not allowed to cross.
    /// </remarks>
    Rule = 10,
}

/// <summary>What a rule condition is about.</summary>
/// <remarks>
/// Every field here is one the catalogue already publishes on <c>ProductProjection</c>, and that is
/// not a coincidence — it is the constraint. A rule that could name something this module cannot see
/// would be a rule this module could not evaluate, and the failure would be a silently empty
/// collection rather than an error anybody could act on.
/// </remarks>
internal enum RuleField
{
    /// <summary>The product's category, matched on the id or on any ancestor's.</summary>
    Category = 0,

    /// <summary>The product's brand.</summary>
    Brand = 10,

    /// <summary>The seller behind the winning offer.</summary>
    Vendor = 20,

    /// <summary>The winning offer's price, inclusive of GST.</summary>
    Price = 30,

    /// <summary>How far below MRP the winning offer sits, as a percentage.</summary>
    DiscountPercent = 40,

    /// <summary>The product's average review score.</summary>
    Rating = 50,

    /// <summary>When the offer went live — the "new in" rule.</summary>
    PublishedWithinDays = 60,

    /// <summary>One of the product's described properties, named by the attribute's code.</summary>
    Attribute = 70,
}

/// <summary>How a condition compares.</summary>
internal enum RuleOperator
{
    /// <summary>Equal to any one of the values. The only operator a text field accepts.</summary>
    In = 0,

    /// <summary>Equal to none of the values.</summary>
    NotIn = 10,

    /// <summary>Greater than the first value.</summary>
    GreaterThan = 20,

    /// <summary>Greater than or equal to the first value.</summary>
    AtLeast = 30,

    /// <summary>Less than the first value.</summary>
    LessThan = 40,

    /// <summary>Less than or equal to the first value.</summary>
    AtMost = 50,
}

/// <summary>
/// One clause of a rule.
/// </summary>
/// <param name="Field">What it is about.</param>
/// <param name="Key">
/// The attribute code, for <see cref="RuleField.Attribute"/>. Ignored by every other field, and
/// required by that one — an attribute condition with no attribute named is a clause that matches
/// everything or nothing depending on how it is read, which is the worst kind of ambiguity to leave
/// in a merchandising rule.
/// </param>
/// <param name="Operator">How it compares.</param>
/// <param name="Values">
/// What it compares against. Text for the identity fields, a decimal for the numeric ones. Held as
/// strings because a rule is stored as one <c>jsonb</c> document and a mixed-type array is a
/// serialisation problem nobody needs.
/// </param>
internal sealed record RuleCondition(
    RuleField Field,
    string? Key,
    RuleOperator Operator,
    IReadOnlyList<string> Values);

/// <summary>How the products a rule found are ordered.</summary>
internal enum CollectionSort
{
    /// <summary>Newest offer first. The default, and what a "new in" collection wants.</summary>
    Newest = 0,

    /// <summary>Cheapest first.</summary>
    PriceAscending = 10,

    /// <summary>Dearest first.</summary>
    PriceDescending = 20,

    /// <summary>Biggest saving first.</summary>
    Discount = 30,

    /// <summary>Best reviewed first.</summary>
    Rating = 40,
}

/// <summary>
/// A rule-based collection's rule, as it is stored (docs/03-database-design.md §4.14).
/// </summary>
/// <param name="MatchAll">
/// Whether a product must satisfy every condition or any one of them. True — every condition — is
/// the default because it is what an editor means when they add a second clause.
/// </param>
/// <param name="Conditions">The clauses.</param>
/// <param name="Sort">How the results are ordered.</param>
/// <param name="Limit">The most products the rule may put in the collection.</param>
/// <param name="IncludeOutOfStock">
/// Whether products with nothing to sell are included. On, and deliberately: a curated collection
/// that silently shrinks because a supplier is late is a page that looks broken, and the storefront
/// can grey out what is unavailable.
/// </param>
internal sealed record CollectionRuleSet(
    bool MatchAll,
    IReadOnlyList<RuleCondition> Conditions,
    CollectionSort Sort,
    int Limit,
    bool IncludeOutOfStock)
{
    /// <summary>The most conditions one rule may carry.</summary>
    /// <remarks>
    /// Each one is evaluated against every product in a catalogue walk. Twelve is far more than a
    /// merchandiser has ever needed and still cheap; a rule with fifty is a query nobody wrote on
    /// purpose.
    /// </remarks>
    public const int MaxConditions = 12;

    /// <summary>The most products a rule may put in one collection.</summary>
    public const int MaxLimit = 500;

    /// <summary>The rule a collection has before anybody writes one: match nothing.</summary>
    /// <remarks>
    /// Nothing rather than everything, and that is the safe default. An unfinished rule that matched
    /// the whole catalogue would put every product the store sells on whatever page the collection
    /// was dropped onto, and it would do it the moment somebody saved a draft.
    /// </remarks>
    public static CollectionRuleSet Empty { get; } = new(true, [], CollectionSort.Newest, 100, true);
}

/// <summary>
/// One product's membership of a collection (docs/03-database-design.md §4.14).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IsPinned"/> is what makes a rule-based collection usable rather than merely automatic.
/// A rule finds the right hundred products and a merchandiser wants three of them at the front for
/// the week; a pinned row keeps its position through every refresh, and the rule fills in behind it.
/// A pin also survives a product falling out of the rule, which is the behaviour an editor expects
/// from something they explicitly chose.
/// </para>
/// <para>
/// The row holds a <c>product_id</c> and nothing else about the product. Everything a card renders —
/// the name, the picture, the winning offer's price — is resolved through
/// <c>IProductProjectionSource</c> when the collection is read, which is what stops a collection tile
/// and the product page it links to naming two different sellers.
/// </para>
/// </remarks>
internal sealed class CollectionItem : Entity<Guid>, ITenantScoped
{
    private CollectionItem(Guid id, Guid collectionId, Guid productId, int position)
        : base(id)
    {
        CollectionId = collectionId;
        ProductId = productId;
        Position = position;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private CollectionItem()
    {
    }

    /// <summary>The collection.</summary>
    public Guid CollectionId { get; private set; }

    /// <summary>The product.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Where it sits, ascending. Pinned rows sort before unpinned ones.</summary>
    public int Position { get; private set; }

    /// <summary>Whether a merchandiser fixed it here. Survives every refresh.</summary>
    public bool IsPinned { get; private set; }

    /// <summary>Whether the rule put it here, rather than a person.</summary>
    /// <remarks>
    /// The column a refresh keys on: it deletes the rows the rule wrote and leaves the rows a person
    /// did. Without it, refreshing a rule-based collection would either delete an editor's pins or
    /// accumulate every product that had ever matched.
    /// </remarks>
    public bool IsFromRule { get; private set; }

    /// <summary>When the row was written, in UTC.</summary>
    public DateTimeOffset AddedAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Adds a product to a collection.</summary>
    /// <param name="collectionId">The collection.</param>
    /// <param name="productId">The product.</param>
    /// <param name="position">Where it sits.</param>
    /// <param name="isPinned">Whether a merchandiser fixed it here.</param>
    /// <param name="isFromRule">Whether the rule put it here.</param>
    /// <param name="addedAt">When.</param>
    public static CollectionItem Create(
        Guid collectionId,
        Guid productId,
        int position,
        bool isPinned,
        bool isFromRule,
        DateTimeOffset addedAt)
        => new(UuidV7.New(), Guard.NotEmpty(collectionId), Guard.NotEmpty(productId), position)
        {
            IsPinned = isPinned,
            IsFromRule = isFromRule,
            AddedAt = addedAt,
        };

    /// <summary>Moves the item, and fixes or releases it.</summary>
    /// <param name="position">Its new position.</param>
    /// <param name="isPinned">Whether it is fixed there.</param>
    public void MoveTo(int position, bool isPinned)
    {
        Position = position;
        IsPinned = isPinned;
    }
}

/// <summary>
/// A curated group of products (docs/03-database-design.md §4.14).
/// </summary>
/// <remarks>
/// <para>
/// The merchandising primitive. A collection is what a carousel block points at, what a menu item
/// links to, and what <c>GET /store/collections/{slug}</c> serves — three surfaces, one definition,
/// which is the whole reason it is a first-class thing rather than a saved search.
/// </para>
/// <para>
/// It has a slug and an SEO block because it is a landing page in its own right. "Under ₹999" is a
/// URL a store puts in a campaign, and a URL a crawler should index, and neither is true of a filter
/// somebody applied on a listing page.
/// </para>
/// <para>
/// The membership rows are not part of this aggregate, and that is deliberate. A collection has a
/// bounded, editable identity — its name, its slug, its rule — and a membership that can run to
/// hundreds of rows and is rewritten wholesale by a background refresh. Loading five hundred items
/// to rename a collection would be the cost of pretending they were one thing.
/// </para>
/// </remarks>
internal sealed class ProductCollection : AggregateRoot<Guid>, ITenantScoped, IAuditable, ISoftDeletable
{
    /// <summary>The longest slug.</summary>
    public const int MaxSlugLength = 200;

    /// <summary>The longest name.</summary>
    public const int MaxNameLength = 200;

    /// <summary>The longest description.</summary>
    public const int MaxDescriptionLength = 2_000;

    private ProductCollection(Guid id, string slug, string name, CollectionKind kind)
        : base(id)
    {
        Slug = slug;
        Name = name;
        Kind = kind;
        Seo = new SeoMetadata();
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ProductCollection()
    {
        Slug = string.Empty;
        Name = string.Empty;
        Seo = new SeoMetadata();
    }

    /// <summary>The URL segment. Unique per tenant among collections that are not deleted.</summary>
    public string Slug { get; private set; }

    /// <summary>What a shopper reads at the top of the page.</summary>
    public string Name { get; private set; }

    /// <summary>The copy beneath the heading.</summary>
    public string? Description { get; private set; }

    /// <summary>How membership is decided.</summary>
    public CollectionKind Kind { get; private set; }

    /// <summary>The rule, as a JSON document. Meaningful only for a rule-based collection.</summary>
    public string Rules { get; private set; } = "{}";

    /// <summary>What a crawler and a social card are told.</summary>
    public SeoMetadata Seo { get; private set; }

    /// <summary>The masthead image.</summary>
    public Guid? HeroImageFileId { get; private set; }

    /// <summary>Whether the storefront serves it.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Whether the sitemap lists it.</summary>
    /// <remarks>
    /// Separate from <see cref="IsActive"/> because they answer different questions. A collection
    /// built for one campaign's landing page is live and should not be indexed; one that is the
    /// store's permanent "sale" page is both.
    /// </remarks>
    public bool IsListed { get; private set; } = true;

    /// <summary>How many products the last refresh put in it. A cached count for the admin list.</summary>
    public int ItemCount { get; private set; }

    /// <summary>When the rule was last evaluated, in UTC. Null for a manual collection.</summary>
    public DateTimeOffset? RefreshedAt { get; private set; }

    /// <summary>When its content last changed, in UTC. What <c>lastmod</c> carries.</summary>
    public DateTimeOffset? ContentChangedAt { get; private set; }

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

    /// <summary>Opens a collection.</summary>
    /// <param name="slug">Its URL segment, already normalised and known to be free.</param>
    /// <param name="name">What a shopper reads.</param>
    /// <param name="kind">How membership is decided.</param>
    public static ProductCollection Create(string slug, string name, CollectionKind kind)
        => new(
            UuidV7.New(),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(slug), MaxSlugLength),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(name), MaxNameLength),
            kind);

    /// <summary>Rewrites the collection's details.</summary>
    /// <param name="slug">Its URL segment, already normalised and known to be free.</param>
    /// <param name="name">What a shopper reads.</param>
    /// <param name="description">The copy beneath the heading.</param>
    /// <param name="seo">What a crawler is told.</param>
    /// <param name="heroImageFileId">The masthead image.</param>
    /// <param name="isActive">Whether the storefront serves it.</param>
    /// <param name="isListed">Whether the sitemap lists it.</param>
    /// <param name="changedAt">When.</param>
    public void Describe(
        string slug,
        string name,
        string? description,
        SeoMetadata seo,
        Guid? heroImageFileId,
        bool isActive,
        bool isListed,
        DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(seo);

        Slug = Guard.MaxLength(Guard.NotNullOrWhiteSpace(slug), MaxSlugLength);
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name), MaxNameLength);
        Description = description;
        Seo = seo;
        HeroImageFileId = heroImageFileId;
        IsActive = isActive;
        IsListed = isListed;
        ContentChangedAt = changedAt;
    }

    /// <summary>
    /// Rewrites the rule, which turns a manual collection into a rule-based one.
    /// </summary>
    /// <remarks>
    /// The change of kind is deliberate rather than a side effect: an editor who writes a rule on a
    /// hand-picked collection means it to start being kept current, and asking them to change a
    /// dropdown as well would be a step whose only purpose is to be forgotten. Their hand-picked
    /// rows survive it — a refresh only ever removes the rows a rule wrote.
    /// </remarks>
    /// <param name="rules">The rule, serialised.</param>
    /// <param name="changedAt">When.</param>
    public void SetRule(string rules, DateTimeOffset changedAt)
    {
        Rules = Guard.NotNullOrWhiteSpace(rules);
        Kind = CollectionKind.Rule;
        ContentChangedAt = changedAt;
    }

    /// <summary>Drops the rule, leaving whatever is in the collection where it is.</summary>
    /// <param name="changedAt">When.</param>
    public void ClearRule(DateTimeOffset changedAt)
    {
        Rules = "{}";
        Kind = CollectionKind.Manual;
        RefreshedAt = null;
        ContentChangedAt = changedAt;
    }

    /// <summary>Records the outcome of a refresh.</summary>
    /// <param name="itemCount">How many products are in it now.</param>
    /// <param name="refreshedAt">When.</param>
    public void RecordRefresh(int itemCount, DateTimeOffset refreshedAt)
    {
        ItemCount = itemCount;
        RefreshedAt = refreshedAt;
        ContentChangedAt = refreshedAt;
    }

    /// <summary>Records that the membership changed by hand.</summary>
    /// <param name="itemCount">How many products are in it now.</param>
    /// <param name="changedAt">When.</param>
    public void RecordMembership(int itemCount, DateTimeOffset changedAt)
    {
        ItemCount = itemCount;
        ContentChangedAt = changedAt;
    }

    /// <summary>Hides the row from every query.</summary>
    /// <param name="deletedAt">When.</param>
    /// <param name="deletedBy">Who.</param>
    public void Delete(DateTimeOffset deletedAt, Guid? deletedBy)
    {
        DeletedAt = deletedAt;
        DeletedBy = deletedBy;
    }
}
