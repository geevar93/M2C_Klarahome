namespace KlaraHome.Modules.Content.Domain;

/// <summary>
/// What a search engine and a social card are told about a page
/// (docs/03-database-design.md §4.14).
/// </summary>
/// <remarks>
/// <para>
/// Stored as <c>jsonb</c> and read whole: nothing joins to it, nothing filters on it, and it is
/// written into a <c>&lt;head&gt;</c> by the storefront almost exactly as it is. That is the narrow
/// case docs/03-database-design.md §1 allows JSON for.
/// </para>
/// <para>
/// Wider than the catalogue's version of the same idea, and deliberately so: the Open Graph fields
/// are here because this is the module that owns the pages people actually share, and a campaign
/// landing page with no <c>og:image</c> is a link that renders as a grey box in every messaging app
/// in India. The duplication with <c>Catalog.Domain.SeoMetadata</c> is the module boundary being
/// paid for, exactly as the slugger is — a shared SEO type would put a presentation concern in the
/// kernel and give two modules a reason to change one class.
/// </para>
/// <para>
/// Every field is optional, and that is the design. A page with no SEO block still renders: the
/// storefront falls back to the page title, the store's tagline and the first image on the page.
/// Making an editor fill six boxes before they can publish a page is how a CMS ends up with six
/// boxes of the same text.
/// </para>
/// </remarks>
internal sealed class SeoMetadata
{
    /// <summary>The longest a meta title may usefully be before a crawler truncates it.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>The longest a meta description may usefully be.</summary>
    public const int MaxDescriptionLength = 400;

    /// <summary>The <c>&lt;title&gt;</c>, or null to fall back to the page's own title.</summary>
    public string? MetaTitle { get; set; }

    /// <summary>The meta description, or null to fall back to the store's tagline.</summary>
    public string? MetaDescription { get; set; }

    /// <summary>Comma-separated keywords. Worth almost nothing to a crawler and cheap to carry.</summary>
    public string? MetaKeywords { get; set; }

    /// <summary>
    /// The canonical URL, when this page duplicates another. Left null for the usual case, where the
    /// storefront derives the canonical from the slug and the configured base URL.
    /// </summary>
    public string? CanonicalUrl { get; set; }

    /// <summary>The Open Graph title, or null to reuse <see cref="MetaTitle"/>.</summary>
    public string? OgTitle { get; set; }

    /// <summary>The Open Graph description, or null to reuse <see cref="MetaDescription"/>.</summary>
    public string? OgDescription { get; set; }

    /// <summary>
    /// The media file behind <c>og:image</c>.
    /// </summary>
    /// <remarks>
    /// A file id rather than a URL, so the image is served through the same renditions and the same
    /// CDN as everything else and cannot rot into a link to somebody's laptop.
    /// </remarks>
    public Guid? OgImageFileId { get; set; }

    /// <summary>The Open Graph type — <c>website</c>, <c>article</c>. Null means the default.</summary>
    public string? OgType { get; set; }

    /// <summary>Whether crawlers are asked to leave the page out of the index.</summary>
    public bool NoIndex { get; set; }

    /// <summary>Whether crawlers are asked not to follow the links on it.</summary>
    public bool NoFollow { get; set; }

    /// <summary>
    /// How strongly this page is offered to a crawler relative to the rest of the site, 0 to 1.
    /// </summary>
    /// <remarks>
    /// Null means "let the sitemap decide from the page type", which is what it should almost always
    /// be. It is here because a campaign page occasionally needs to be shouted about, and because
    /// leaving it out would send every editor to a plugin.
    /// </remarks>
    public decimal? SitemapPriority { get; set; }

    /// <summary>Whether anything has actually been filled in.</summary>
    public bool IsEmpty
        => string.IsNullOrWhiteSpace(MetaTitle)
           && string.IsNullOrWhiteSpace(MetaDescription)
           && string.IsNullOrWhiteSpace(MetaKeywords)
           && string.IsNullOrWhiteSpace(CanonicalUrl)
           && string.IsNullOrWhiteSpace(OgTitle)
           && string.IsNullOrWhiteSpace(OgDescription)
           && OgImageFileId is null
           && !NoIndex
           && !NoFollow;

    /// <summary>A field-by-field copy, so a version snapshot cannot share a reference with the page.</summary>
    /// <remarks>
    /// The bug this prevents is quiet and expensive: a snapshot holding the same object as the live
    /// page would follow every later edit, and a rollback would restore the state it was rolling
    /// back from.
    /// </remarks>
    public SeoMetadata Clone()
        => new()
        {
            MetaTitle = MetaTitle,
            MetaDescription = MetaDescription,
            MetaKeywords = MetaKeywords,
            CanonicalUrl = CanonicalUrl,
            OgTitle = OgTitle,
            OgDescription = OgDescription,
            OgImageFileId = OgImageFileId,
            OgType = OgType,
            NoIndex = NoIndex,
            NoFollow = NoFollow,
            SitemapPriority = SitemapPriority,
        };
}
