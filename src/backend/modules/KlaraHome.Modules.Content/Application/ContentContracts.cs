using System.Text.Json;
using KlaraHome.Modules.Content.Domain;

namespace KlaraHome.Modules.Content.Application;

/// <summary>One rendition of an image, as the storefront asks for it.</summary>
/// <param name="Name">The rendition key — <c>thumb</c>, <c>small</c>, <c>medium</c>, <c>large</c>.</param>
/// <param name="Width">Target width in CSS pixels.</param>
/// <param name="Url">A ready-to-use URL.</param>
internal sealed record ContentImageVariantResponse(string Name, int Width, string Url);

/// <summary>
/// An image a block, banner or card renders.
/// </summary>
/// <remarks>
/// Resolved here rather than left as a file id, and that is the point of it. A storefront handed
/// bare ids would have to make one call per image to turn a home page into something it can render,
/// and server-side rendering would then be waiting on twenty round trips before it emitted a byte.
/// </remarks>
/// <param name="FileId">The stored file.</param>
/// <param name="Url">Its canonical URL, or null when the file has been deleted.</param>
/// <param name="Width">Pixel width, when it is a raster image.</param>
/// <param name="Height">Pixel height, when it is a raster image.</param>
/// <param name="Alt">The alt text, from whichever record referenced it.</param>
/// <param name="Variants">Responsive renditions.</param>
internal sealed record ContentImageResponse(
    Guid FileId,
    string? Url,
    int? Width,
    int? Height,
    string? Alt,
    IReadOnlyList<ContentImageVariantResponse> Variants);

/// <summary>What a crawler and a social card are told about a page.</summary>
/// <param name="MetaTitle">The title, or null to fall back to the page's own.</param>
/// <param name="MetaDescription">The description.</param>
/// <param name="MetaKeywords">Comma-separated keywords.</param>
/// <param name="CanonicalUrl">The canonical URL, when this page duplicates another.</param>
/// <param name="OgTitle">The Open Graph title.</param>
/// <param name="OgDescription">The Open Graph description.</param>
/// <param name="OgImage">The Open Graph image, resolved.</param>
/// <param name="OgType">The Open Graph type.</param>
/// <param name="NoIndex">Whether crawlers are asked to leave it out of the index.</param>
/// <param name="NoFollow">Whether crawlers are asked not to follow its links.</param>
/// <param name="SitemapPriority">How strongly it is offered to a crawler, 0 to 1.</param>
internal sealed record SeoResponse(
    string? MetaTitle,
    string? MetaDescription,
    string? MetaKeywords,
    string? CanonicalUrl,
    string? OgTitle,
    string? OgDescription,
    ContentImageResponse? OgImage,
    string? OgType,
    bool NoIndex,
    bool NoFollow,
    decimal? SitemapPriority);

/// <summary>One field of a block type's schema, as the admin block editor reads it.</summary>
/// <param name="Name">The JSON property name.</param>
/// <param name="Kind">What it holds.</param>
/// <param name="IsRequired">Whether a block without it is refused.</param>
/// <param name="IsList">Whether the value is an array.</param>
/// <param name="MaxLength">The longest a text value may be, or the most items a list may hold.</param>
/// <param name="Choices">The words accepted, for a choice field.</param>
internal sealed record BlockFieldResponse(
    string Name,
    string Kind,
    bool IsRequired,
    bool IsList,
    int MaxLength,
    IReadOnlyList<string>? Choices);

/// <summary>
/// One block type, as the admin block picker reads it.
/// </summary>
/// <remarks>
/// The schema is served rather than compiled into the admin app, so the two cannot drift: a block
/// type whose fields changed in a release would otherwise be edited through last release's form and
/// refused by this release's validator, with nothing on screen explaining why.
/// </remarks>
/// <param name="Type">The block type's name.</param>
/// <param name="Label">What an editor sees in the picker.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Fields">Its fields.</param>
/// <param name="ItemFields">The fields of a repeated child, or null when it has none.</param>
/// <param name="MaxItems">The most children it may hold.</param>
/// <param name="IsPrivileged">Whether writing it needs the custom-HTML permission.</param>
internal sealed record BlockTypeResponse(
    string Type,
    string Label,
    string Description,
    IReadOnlyList<BlockFieldResponse> Fields,
    IReadOnlyList<BlockFieldResponse>? ItemFields,
    int MaxItems,
    bool IsPrivileged);

/// <summary>One block of a page, as an editor sees it.</summary>
/// <param name="Id">The block.</param>
/// <param name="Type">Its type.</param>
/// <param name="Position">Where it sits, ascending from zero.</param>
/// <param name="Config">Its configuration document.</param>
/// <param name="IsVisible">Whether it is rendered.</param>
/// <param name="StartsAt">When it starts appearing.</param>
/// <param name="EndsAt">When it stops.</param>
internal sealed record BlockResponse(
    Guid Id,
    string Type,
    int Position,
    JsonElement Config,
    bool IsVisible,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

/// <summary>A page in an editor's list.</summary>
/// <param name="Id">The page.</param>
/// <param name="Slug">Its address.</param>
/// <param name="Type">What it is for.</param>
/// <param name="Title">Its title.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="PublishedAt">When it first went live.</param>
/// <param name="ScheduledAt">When it is due to go live.</param>
/// <param name="ContentChangedAt">When its content last changed.</param>
/// <param name="Version">How many versions have been snapshotted.</param>
/// <param name="BlockCount">How many blocks it holds.</param>
/// <param name="UpdatedAt">When the row last changed.</param>
internal sealed record PageSummaryResponse(
    Guid Id,
    string Slug,
    PageType Type,
    string Title,
    PageStatus Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? ContentChangedAt,
    int Version,
    int BlockCount,
    DateTimeOffset? UpdatedAt);

/// <summary>A page in full, as an editor sees it.</summary>
/// <param name="Id">The page.</param>
/// <param name="Slug">Its address.</param>
/// <param name="Type">What it is for.</param>
/// <param name="Title">Its title.</param>
/// <param name="Summary">Its summary.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="PublishedAt">When it first went live.</param>
/// <param name="ScheduledAt">When it is due to go live.</param>
/// <param name="ContentChangedAt">When its content last changed.</param>
/// <param name="Version">How many versions have been snapshotted.</param>
/// <param name="Seo">What a crawler is told.</param>
/// <param name="CoverImage">Its cover image, resolved.</param>
/// <param name="Author">The byline, for a blog page.</param>
/// <param name="Tags">Its tags.</param>
/// <param name="Blocks">Its blocks, in position order.</param>
/// <param name="AllowedTransitions">
/// Where this caller may move it from here, straight off the transition table — which is what the
/// admin screen draws its buttons from, so a button that exists is a button that works.
/// </param>
/// <param name="CreatedAt">When it was opened.</param>
/// <param name="UpdatedAt">When the row last changed.</param>
internal sealed record PageResponse(
    Guid Id,
    string Slug,
    PageType Type,
    string Title,
    string? Summary,
    PageStatus Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? ContentChangedAt,
    int Version,
    SeoResponse Seo,
    ContentImageResponse? CoverImage,
    string? Author,
    IReadOnlyList<string> Tags,
    IReadOnlyList<BlockResponse> Blocks,
    IReadOnlyList<PageStatus> AllowedTransitions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>One entry in a page's history.</summary>
/// <param name="Version">Which version it is.</param>
/// <param name="Title">The title as it stood.</param>
/// <param name="Note">Why it exists.</param>
/// <param name="RestoredFrom">The version restored, when it was a rollback.</param>
/// <param name="BlockCount">How many blocks it holds.</param>
/// <param name="CreatedAt">When it was taken.</param>
/// <param name="CreatedBy">Who took it.</param>
internal sealed record PageVersionSummaryResponse(
    int Version,
    string Title,
    string? Note,
    int? RestoredFrom,
    int BlockCount,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy);

/// <summary>One version of a page, in full.</summary>
/// <param name="Version">Which version it is.</param>
/// <param name="Title">The title as it stood.</param>
/// <param name="Seo">The SEO block as it stood.</param>
/// <param name="Blocks">The blocks as they stood.</param>
/// <param name="Note">Why it exists.</param>
/// <param name="RestoredFrom">The version restored, when it was a rollback.</param>
/// <param name="CreatedAt">When it was taken.</param>
/// <param name="CreatedBy">Who took it.</param>
internal sealed record PageVersionResponse(
    int Version,
    string Title,
    SeoResponse Seo,
    IReadOnlyList<BlockResponse> Blocks,
    string? Note,
    int? RestoredFrom,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy);

/// <summary>One item of a navigation menu, as an editor sees it.</summary>
/// <param name="Id">The item.</param>
/// <param name="ParentId">The item above it.</param>
/// <param name="Label">What a shopper reads.</param>
/// <param name="LinkType">What it points at.</param>
/// <param name="TargetId">The page, category or collection.</param>
/// <param name="Url">The literal URL.</param>
/// <param name="Position">Where it sits among its siblings.</param>
/// <param name="Depth">How deep it is.</param>
/// <param name="IsVisible">Whether it is rendered.</param>
/// <param name="OpensInNewTab">Whether it opens in a new tab.</param>
/// <param name="IconFileId">Its icon.</param>
/// <param name="Badge">Its badge.</param>
internal sealed record MenuItemResponse(
    Guid Id,
    Guid? ParentId,
    string Label,
    MenuLinkType LinkType,
    Guid? TargetId,
    string? Url,
    int Position,
    int Depth,
    bool IsVisible,
    bool OpensInNewTab,
    Guid? IconFileId,
    string? Badge);

/// <summary>A menu in an editor's list.</summary>
/// <param name="Id">The menu.</param>
/// <param name="Code">Its stable key.</param>
/// <param name="Name">What an editor calls it.</param>
/// <param name="Placement">Where the storefront renders it.</param>
/// <param name="IsActive">Whether it is served.</param>
/// <param name="ItemCount">How many items it holds.</param>
/// <param name="UpdatedAt">When it last changed.</param>
internal sealed record MenuSummaryResponse(
    Guid Id,
    string Code,
    string Name,
    string? Placement,
    bool IsActive,
    int ItemCount,
    DateTimeOffset? UpdatedAt);

/// <summary>A menu in full, as an editor sees it.</summary>
/// <param name="Id">The menu.</param>
/// <param name="Code">Its stable key.</param>
/// <param name="Name">What an editor calls it.</param>
/// <param name="Placement">Where the storefront renders it.</param>
/// <param name="IsActive">Whether it is served.</param>
/// <param name="Items">Its items, parents before their children.</param>
/// <param name="CreatedAt">When it was opened.</param>
/// <param name="UpdatedAt">When it last changed.</param>
internal sealed record MenuResponse(
    Guid Id,
    string Code,
    string Name,
    string? Placement,
    bool IsActive,
    IReadOnlyList<MenuItemResponse> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>A banner, as an editor sees it.</summary>
/// <param name="Id">The banner.</param>
/// <param name="Name">What an editor calls it.</param>
/// <param name="Placement">Where it appears.</param>
/// <param name="Image">The desktop image, resolved.</param>
/// <param name="MobileImage">The mobile image, resolved.</param>
/// <param name="Message">The words, for an announcement bar.</param>
/// <param name="AltText">Its alt text.</param>
/// <param name="Link">Where clicking it goes.</param>
/// <param name="CtaLabel">The button's wording.</param>
/// <param name="Priority">Which banner wins the placement.</param>
/// <param name="StartsAt">When it starts.</param>
/// <param name="EndsAt">When it stops.</param>
/// <param name="Audience">Who sees it.</param>
/// <param name="IsActive">Whether it is switched on.</param>
/// <param name="IsLive">Whether it is being rendered right now.</param>
/// <param name="UpdatedAt">When it last changed.</param>
internal sealed record BannerResponse(
    Guid Id,
    string Name,
    BannerPlacement Placement,
    ContentImageResponse? Image,
    ContentImageResponse? MobileImage,
    string? Message,
    string? AltText,
    string? Link,
    string? CtaLabel,
    int Priority,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    BannerAudience Audience,
    bool IsActive,
    bool IsLive,
    DateTimeOffset? UpdatedAt);

/// <summary>One clause of a collection's rule.</summary>
/// <param name="Field">What it is about.</param>
/// <param name="Key">The attribute code, for an attribute condition.</param>
/// <param name="Operator">How it compares.</param>
/// <param name="Values">What it compares against.</param>
internal sealed record RuleConditionResponse(
    RuleField Field,
    string? Key,
    RuleOperator Operator,
    IReadOnlyList<string> Values);

/// <summary>A collection's rule.</summary>
/// <param name="MatchAll">Whether every condition must hold, or any one of them.</param>
/// <param name="Conditions">The clauses.</param>
/// <param name="Sort">How the results are ordered.</param>
/// <param name="Limit">The most products the rule may add.</param>
/// <param name="IncludeOutOfStock">Whether products with nothing to sell are included.</param>
internal sealed record CollectionRuleResponse(
    bool MatchAll,
    IReadOnlyList<RuleConditionResponse> Conditions,
    CollectionSort Sort,
    int Limit,
    bool IncludeOutOfStock);

/// <summary>A collection in an editor's list.</summary>
/// <param name="Id">The collection.</param>
/// <param name="Slug">Its address.</param>
/// <param name="Name">What a shopper reads.</param>
/// <param name="Kind">How membership is decided.</param>
/// <param name="IsActive">Whether the storefront serves it.</param>
/// <param name="IsListed">Whether the sitemap lists it.</param>
/// <param name="ItemCount">How many products are in it.</param>
/// <param name="RefreshedAt">When the rule was last evaluated.</param>
/// <param name="UpdatedAt">When it last changed.</param>
internal sealed record CollectionSummaryResponse(
    Guid Id,
    string Slug,
    string Name,
    CollectionKind Kind,
    bool IsActive,
    bool IsListed,
    int ItemCount,
    DateTimeOffset? RefreshedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>A collection in full, as an editor sees it.</summary>
/// <param name="Id">The collection.</param>
/// <param name="Slug">Its address.</param>
/// <param name="Name">What a shopper reads.</param>
/// <param name="Description">The copy beneath the heading.</param>
/// <param name="Kind">How membership is decided.</param>
/// <param name="Rule">Its rule, or null when it is hand-picked.</param>
/// <param name="Seo">What a crawler is told.</param>
/// <param name="HeroImage">The masthead image, resolved.</param>
/// <param name="IsActive">Whether the storefront serves it.</param>
/// <param name="IsListed">Whether the sitemap lists it.</param>
/// <param name="ItemCount">How many products are in it.</param>
/// <param name="RefreshedAt">When the rule was last evaluated.</param>
/// <param name="CreatedAt">When it was opened.</param>
/// <param name="UpdatedAt">When it last changed.</param>
internal sealed record CollectionResponse(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    CollectionKind Kind,
    CollectionRuleResponse? Rule,
    SeoResponse Seo,
    ContentImageResponse? HeroImage,
    bool IsActive,
    bool IsListed,
    int ItemCount,
    DateTimeOffset? RefreshedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// A product on a card, wherever the CMS renders one.
/// </summary>
/// <remarks>
/// The buy box is resolved by the Catalog module and carried here, so a carousel tile and the
/// product page it links to name the same seller at the same price. A CMS that picked its own — the
/// cheapest offer, say — would eventually disagree with the page, and nobody would be able to say
/// which of the two was wrong.
/// </remarks>
/// <param name="ProductId">The product.</param>
/// <param name="VariantId">The variant the card is for.</param>
/// <param name="ListingId">The offer that won the buy box.</param>
/// <param name="Name">The product's title.</param>
/// <param name="Slug">Its URL segment.</param>
/// <param name="BrandName">Its brand.</param>
/// <param name="Mrp">Maximum retail price.</param>
/// <param name="Price">The winning offer's price, inclusive of GST.</param>
/// <param name="CurrencyCode">The currency both amounts are in.</param>
/// <param name="DiscountPercent">How far below MRP the price sits.</param>
/// <param name="RatingAverage">The product's average review score.</param>
/// <param name="RatingCount">How many reviews that is over.</param>
/// <param name="Image">Its primary image, resolved.</param>
/// <param name="IsPurchasable">Whether it can be bought right now.</param>
internal sealed record ProductCardResponse(
    Guid ProductId,
    Guid VariantId,
    Guid ListingId,
    string Name,
    string Slug,
    string? BrandName,
    decimal Mrp,
    decimal Price,
    string CurrencyCode,
    decimal DiscountPercent,
    decimal? RatingAverage,
    int RatingCount,
    ContentImageResponse? Image,
    bool IsPurchasable);

/// <summary>A category tile, wherever the CMS renders one.</summary>
/// <param name="CategoryId">The category.</param>
/// <param name="Name">Its name.</param>
/// <param name="Slug">Its URL segment.</param>
/// <param name="Image">Its tile image, resolved.</param>
internal sealed record CategoryTileResponse(
    Guid CategoryId,
    string Name,
    string Slug,
    ContentImageResponse? Image);

/// <summary>
/// One block of a page, resolved and ready to render.
/// </summary>
/// <remarks>
/// The configuration document is carried through as it was written, and the things it <em>refers</em>
/// to arrive alongside it already resolved. That split is what keeps the storefront component simple:
/// it reads its own fields out of <paramref name="Config"/> and looks up any id it finds in the three
/// lists, without a second call and without knowing which module owned the answer.
/// </remarks>
/// <param name="Id">The block.</param>
/// <param name="Type">Its type.</param>
/// <param name="Position">Where it sits.</param>
/// <param name="Config">Its configuration document, as written.</param>
/// <param name="Images">Every media file the block refers to, resolved.</param>
/// <param name="Products">Every product it refers to, resolved, in the order it named them.</param>
/// <param name="Categories">Every category it refers to, resolved.</param>
internal sealed record StoreBlockResponse(
    Guid Id,
    string Type,
    int Position,
    JsonElement Config,
    IReadOnlyList<ContentImageResponse> Images,
    IReadOnlyList<ProductCardResponse> Products,
    IReadOnlyList<CategoryTileResponse> Categories);

/// <summary>A page, as the storefront renders it.</summary>
/// <param name="Id">The page.</param>
/// <param name="Slug">Its address.</param>
/// <param name="Type">What it is for.</param>
/// <param name="Title">Its title.</param>
/// <param name="Summary">Its summary.</param>
/// <param name="Seo">What goes in the head.</param>
/// <param name="CoverImage">Its cover image, resolved.</param>
/// <param name="Author">The byline, for a blog page.</param>
/// <param name="Tags">Its tags.</param>
/// <param name="PublishedAt">When it first went live.</param>
/// <param name="UpdatedAt">When its content last changed.</param>
/// <param name="Blocks">Its blocks, in position order, windowed to this instant.</param>
internal sealed record StorePageResponse(
    Guid Id,
    string Slug,
    PageType Type,
    string Title,
    string? Summary,
    SeoResponse Seo,
    ContentImageResponse? CoverImage,
    string? Author,
    IReadOnlyList<string> Tags,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<StoreBlockResponse> Blocks);

/// <summary>A page in the blog index.</summary>
/// <param name="Slug">Its address.</param>
/// <param name="Title">Its title.</param>
/// <param name="Summary">Its excerpt.</param>
/// <param name="Author">The byline.</param>
/// <param name="Tags">Its tags.</param>
/// <param name="CoverImage">Its cover image, resolved.</param>
/// <param name="PublishedAt">When it went live.</param>
internal sealed record BlogCardResponse(
    string Slug,
    string Title,
    string? Summary,
    string? Author,
    IReadOnlyList<string> Tags,
    ContentImageResponse? CoverImage,
    DateTimeOffset? PublishedAt);

/// <summary>One item of a menu, as the storefront renders it.</summary>
/// <param name="Label">What a shopper reads.</param>
/// <param name="Href">
/// Where it goes, already resolved from the link type and its target. Null for a heading that is
/// not itself a link.
/// </param>
/// <param name="OpensInNewTab">Whether it opens in a new tab.</param>
/// <param name="Icon">Its icon, resolved.</param>
/// <param name="Badge">Its badge.</param>
/// <param name="Children">The items beneath it, in position order.</param>
internal sealed record StoreMenuItemResponse(
    string Label,
    string? Href,
    bool OpensInNewTab,
    ContentImageResponse? Icon,
    string? Badge,
    IReadOnlyList<StoreMenuItemResponse> Children);

/// <summary>A menu, as the storefront renders it.</summary>
/// <param name="Code">Its stable key.</param>
/// <param name="Name">What an editor calls it.</param>
/// <param name="Placement">Where it renders.</param>
/// <param name="Items">Its top-level items, each carrying its own children.</param>
internal sealed record StoreMenuResponse(
    string Code,
    string Name,
    string? Placement,
    IReadOnlyList<StoreMenuItemResponse> Items);

/// <summary>A banner, as the storefront renders it.</summary>
/// <param name="Id">The banner.</param>
/// <param name="Placement">Where it appears.</param>
/// <param name="Image">The desktop image, resolved.</param>
/// <param name="MobileImage">The mobile image, resolved.</param>
/// <param name="Message">The words, for an announcement bar.</param>
/// <param name="AltText">Its alt text.</param>
/// <param name="Link">Where clicking it goes.</param>
/// <param name="CtaLabel">The button's wording.</param>
/// <param name="Priority">Which banner wins the placement, highest first.</param>
internal sealed record StoreBannerResponse(
    Guid Id,
    BannerPlacement Placement,
    ContentImageResponse? Image,
    ContentImageResponse? MobileImage,
    string? Message,
    string? AltText,
    string? Link,
    string? CtaLabel,
    int Priority);

/// <summary>A collection's landing page, as the storefront renders it.</summary>
/// <param name="Slug">Its address.</param>
/// <param name="Name">What a shopper reads.</param>
/// <param name="Description">The copy beneath the heading.</param>
/// <param name="Seo">What goes in the head.</param>
/// <param name="HeroImage">The masthead image, resolved.</param>
/// <param name="ItemCount">How many products are in it altogether.</param>
/// <param name="Products">This page of them, in the collection's own order.</param>
/// <param name="NextCursor">The token for the next page, or null.</param>
internal sealed record StoreCollectionResponse(
    string Slug,
    string Name,
    string? Description,
    SeoResponse Seo,
    ContentImageResponse? HeroImage,
    int ItemCount,
    IReadOnlyList<ProductCardResponse> Products,
    string? NextCursor);

/// <summary>A redirect, as an editor sees it.</summary>
/// <param name="Id">The rule.</param>
/// <param name="FromPath">The path being asked for.</param>
/// <param name="ToPath">Where it goes.</param>
/// <param name="StatusCode">What the storefront answers with.</param>
/// <param name="HitCount">How many times it has fired.</param>
/// <param name="LastHitAt">When it last fired.</param>
/// <param name="IsActive">Whether it is applied.</param>
/// <param name="Note">Why it exists.</param>
/// <param name="CreatedAt">When it was written.</param>
internal sealed record RedirectResponse(
    Guid Id,
    string FromPath,
    string? ToPath,
    int StatusCode,
    long HitCount,
    DateTimeOffset? LastHitAt,
    bool IsActive,
    string? Note,
    DateTimeOffset CreatedAt);

/// <summary>
/// What the storefront should do with a path nothing else claims.
/// </summary>
/// <remarks>
/// A dedicated shape rather than an HTTP redirect from the API, because the API is not the thing
/// being browsed. The storefront asks this on a 404 and then issues the redirect itself, with its own
/// status code, from its own origin — which is the only way the browser's address bar ends up right.
/// </remarks>
/// <param name="StatusCode">301, 302 or 410.</param>
/// <param name="Location">Where to go, or null for a 410.</param>
internal sealed record RedirectResolutionResponse(int StatusCode, string? Location);

/// <summary>One URL in a sitemap.</summary>
/// <param name="Loc">The absolute URL.</param>
/// <param name="LastModified">When the thing behind it last changed.</param>
/// <param name="ChangeFrequency">How often it is expected to change.</param>
/// <param name="Priority">How strongly it is offered, 0 to 1.</param>
internal sealed record SitemapUrlResponse(
    string Loc,
    DateTimeOffset? LastModified,
    string ChangeFrequency,
    decimal Priority);

/// <summary>One page of one section of the sitemap.</summary>
/// <param name="Section">Which section — <c>pages</c>, <c>collections</c>, <c>categories</c>…</param>
/// <param name="Page">Which page of it, one-based.</param>
/// <param name="Urls">The URLs.</param>
internal sealed record SitemapPageResponse(string Section, int Page, IReadOnlyList<SitemapUrlResponse> Urls);

/// <summary>One entry of the sitemap index.</summary>
/// <param name="Section">Which section.</param>
/// <param name="Page">Which page of it, one-based.</param>
/// <param name="Loc">The absolute URL of that page's sitemap.</param>
/// <param name="UrlCount">How many URLs it carries.</param>
/// <param name="LastModified">The newest <c>lastmod</c> among them.</param>
internal sealed record SitemapIndexEntryResponse(
    string Section,
    int Page,
    string Loc,
    int UrlCount,
    DateTimeOffset? LastModified);

/// <summary>
/// The sitemap index: which sitemaps exist, and where.
/// </summary>
/// <remarks>
/// An index rather than one file, because the protocol caps a sitemap at fifty thousand URLs and a
/// store with a real catalogue passes that. Paginating from the start also means the products
/// section can grow without the pages section ever changing, which is what keeps a crawler's
/// conditional fetches cheap.
/// </remarks>
/// <param name="Entries">The sitemaps.</param>
internal sealed record SitemapIndexResponse(IReadOnlyList<SitemapIndexEntryResponse> Entries);

/// <summary>
/// The structured-data graph for one page (schema.org, JSON-LD).
/// </summary>
/// <remarks>
/// A graph rather than a list of documents. <c>Organization</c> and <c>WebSite</c> are on every page,
/// and a <c>Product</c> refers to the organisation as its seller — expressed as one
/// <c>@graph</c> with <c>@id</c> references, which is how a crawler is meant to be told that the two
/// nodes are the same organisation rather than two similarly named ones.
/// </remarks>
/// <param name="Path">The path the graph describes.</param>
/// <param name="Graph">The JSON-LD document, ready to be written into a script tag.</param>
internal sealed record StructuredDataResponse(string Path, JsonElement Graph);

/// <summary>The public SEO surface the storefront needs before it renders anything.</summary>
/// <param name="CanonicalBaseUrl">The origin every absolute URL is built from.</param>
/// <param name="AllowIndexing">Whether this deployment wants to be indexed at all.</param>
/// <param name="TitleTemplate">The template a page title falls back to.</param>
/// <param name="DefaultMetaDescription">The description a page with none falls back to.</param>
/// <param name="TwitterCardType">The card type to declare.</param>
/// <param name="RobotsUrl">Where the robots document is served from.</param>
/// <param name="SitemapUrl">Where the sitemap index is served from.</param>
internal sealed record SeoConfigResponse(
    string CanonicalBaseUrl,
    bool AllowIndexing,
    string TitleTemplate,
    string DefaultMetaDescription,
    string TwitterCardType,
    string RobotsUrl,
    string SitemapUrl);
