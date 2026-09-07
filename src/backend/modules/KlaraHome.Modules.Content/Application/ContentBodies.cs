using System.Text.Json;

namespace KlaraHome.Modules.Content.Application;

/// <summary>
/// What a caller sends for a page's SEO block.
/// </summary>
/// <remarks>
/// Every field optional, and that is the design. A page with no SEO block still renders — the
/// storefront falls back to the page title, the store's tagline and the first image on the page —
/// and making an editor fill six boxes before they can publish is how a CMS ends up with six boxes of
/// the same text.
/// </remarks>
/// <param name="MetaTitle">The title.</param>
/// <param name="MetaDescription">The description.</param>
/// <param name="MetaKeywords">Comma-separated keywords.</param>
/// <param name="CanonicalUrl">The canonical URL, when this page duplicates another.</param>
/// <param name="OgTitle">The Open Graph title.</param>
/// <param name="OgDescription">The Open Graph description.</param>
/// <param name="OgImageFileId">The Open Graph image.</param>
/// <param name="OgType">The Open Graph type.</param>
/// <param name="NoIndex">Whether crawlers are asked to leave it out of the index.</param>
/// <param name="NoFollow">Whether crawlers are asked not to follow its links.</param>
/// <param name="SitemapPriority">How strongly it is offered to a crawler, 0 to 1.</param>
internal sealed record SeoBody(
    string? MetaTitle,
    string? MetaDescription,
    string? MetaKeywords,
    string? CanonicalUrl,
    string? OgTitle,
    string? OgDescription,
    Guid? OgImageFileId,
    string? OgType,
    bool NoIndex,
    bool NoFollow,
    decimal? SitemapPriority);

/// <summary>
/// One block, as a caller sends it.
/// </summary>
/// <remarks>
/// The id is optional and it is what makes an edit an edit. A block sent with the id it already had
/// keeps that identity through the save; one sent without gets a new one. That matters because a
/// version snapshot records ids, so a rollback can only restore a block that still has the id the
/// snapshot named.
/// </remarks>
/// <param name="Id">The block, when it already exists.</param>
/// <param name="Type">Its type.</param>
/// <param name="Config">Its configuration, matching the type's schema.</param>
/// <param name="IsVisible">Whether it is rendered.</param>
/// <param name="StartsAt">When it starts appearing.</param>
/// <param name="EndsAt">When it stops.</param>
internal sealed record BlockBody(
    Guid? Id,
    string? Type,
    JsonElement Config,
    bool IsVisible,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

/// <summary>One menu item, as a caller sends it.</summary>
/// <remarks>
/// A flat list with parents named by id rather than a nested tree, and parents must appear before
/// their children. A nested body would be tidier to read and considerably worse to validate: depth,
/// ordering and orphaning are all one pass over a flat list and three recursive walks over a tree.
/// </remarks>
/// <param name="Id">The item, when it already exists.</param>
/// <param name="ParentId">The item above it, or null for a top-level item.</param>
/// <param name="Label">What a shopper reads.</param>
/// <param name="LinkType">What it points at.</param>
/// <param name="TargetId">The page, category or collection.</param>
/// <param name="Url">The literal path or URL.</param>
/// <param name="IsVisible">Whether it is rendered.</param>
/// <param name="OpensInNewTab">Whether it opens in a new tab.</param>
/// <param name="IconFileId">Its icon.</param>
/// <param name="Badge">Its badge.</param>
internal sealed record MenuItemBody(
    Guid? Id,
    Guid? ParentId,
    string? Label,
    string? LinkType,
    Guid? TargetId,
    string? Url,
    bool IsVisible,
    bool OpensInNewTab,
    Guid? IconFileId,
    string? Badge);

/// <summary>One clause of a collection's rule, as a caller sends it.</summary>
/// <param name="Field">What it is about.</param>
/// <param name="Key">The attribute code, for an attribute condition.</param>
/// <param name="Operator">How it compares.</param>
/// <param name="Values">What it compares against.</param>
internal sealed record RuleConditionBody(
    string? Field,
    string? Key,
    string? Operator,
    IReadOnlyList<string>? Values);

/// <summary>A collection's rule, as a caller sends it.</summary>
/// <param name="MatchAll">Whether every condition must hold, or any one of them.</param>
/// <param name="Conditions">The clauses.</param>
/// <param name="Sort">How the results are ordered.</param>
/// <param name="Limit">The most products the rule may add.</param>
/// <param name="IncludeOutOfStock">Whether products with nothing to sell are included.</param>
internal sealed record CollectionRuleBody(
    bool MatchAll,
    IReadOnlyList<RuleConditionBody>? Conditions,
    string? Sort,
    int Limit,
    bool IncludeOutOfStock);

/// <summary>One product's place in a hand-picked collection, as a caller sends it.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="IsPinned">Whether it is fixed where it is, through every refresh.</param>
internal sealed record CollectionItemBody(Guid ProductId, bool IsPinned);
