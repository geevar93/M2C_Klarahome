namespace KlaraHome.Modules.Catalog.Domain;

/// <summary>
/// What a search engine is told about a page (docs/03-database-design.md §4.4).
/// </summary>
/// <remarks>
/// Stored as <c>jsonb</c> and read whole: nothing joins to it, nothing filters on it, and it is
/// written into a <c>&lt;head&gt;</c> by the storefront exactly as it is. That is the narrow case
/// §1 allows JSON for.
/// </remarks>
internal sealed class SeoMetadata
{
    /// <summary>The <c>&lt;title&gt;</c>, or null to fall back to the entity's own name.</summary>
    public string? MetaTitle { get; set; }

    /// <summary>The meta description, or null to fall back to the short description.</summary>
    public string? MetaDescription { get; set; }

    /// <summary>Comma-separated keywords. Worth almost nothing to a crawler and cheap to carry.</summary>
    public string? MetaKeywords { get; set; }

    /// <summary>
    /// The canonical URL, when this page is a duplicate of another. Left null for the usual case,
    /// where the storefront derives the canonical from the slug.
    /// </summary>
    public string? CanonicalUrl { get; set; }

    /// <summary>Whether crawlers are asked to leave the page out of the index.</summary>
    public bool NoIndex { get; set; }
}

/// <summary>
/// A named party on a package, as Legal Metrology requires it to be declared
/// (docs/02-domain-model.md §7.4).
/// </summary>
/// <remarks>
/// The manufacturer, the packer and the importer are the same shape and are declared separately
/// because a product may have all three and they may be different companies. Stored as
/// <c>jsonb</c>: it is printed on a product page and never queried into.
/// </remarks>
internal sealed class PartyDetails
{
    /// <summary>The company's name, as it appears on the pack.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Their full address, as one block, exactly as it must be displayed.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>A contact number or mailbox for consumer care, where one is declared.</summary>
    public string? Contact { get; set; }

    /// <summary>Whether anything has actually been declared.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Address);
}
