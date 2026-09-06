using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Catalog.Domain;

/// <summary>
/// A manufacturer's or house label's brand (docs/03-database-design.md §4.4).
/// </summary>
/// <remarks>
/// A first-class row rather than a string on the product, because the brand is a filter facet, a
/// landing page and a merchandising unit. Free text would give the storefront "Philips", "philips"
/// and "Phillips" as three brands and no way to notice.
/// </remarks>
internal sealed class Brand : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private Brand(Guid id, string name, string slug)
        : base(id)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Slug = Guard.NotNullOrWhiteSpace(slug);
        Seo = new SeoMetadata();
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Brand()
    {
        Name = string.Empty;
        Slug = string.Empty;
        Seo = new SeoMetadata();
    }

    /// <summary>The brand name, as it is printed.</summary>
    public string Name { get; private set; }

    /// <summary>The URL path segment for the brand page. Unique per tenant.</summary>
    public string Slug { get; private set; }

    /// <summary>The brand story shown on that page.</summary>
    public string? Description { get; private set; }

    /// <summary>The logo, as a <c>media.files</c> id. A soft reference, never a foreign key.</summary>
    public Guid? LogoFileId { get; private set; }

    /// <summary>Whether the brand is offered on the storefront.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>What a crawler is told about the brand page.</summary>
    public SeoMetadata Seo { get; private set; }

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

    /// <summary>Registers a brand.</summary>
    /// <param name="name">The brand name.</param>
    /// <param name="slug">The URL segment, already normalised and known to be free.</param>
    public static Brand Create(string name, string slug) => new(UuidV7.New(), name, slug);

    /// <summary>Updates the brand's presentation.</summary>
    /// <param name="name">The brand name.</param>
    /// <param name="slug">The URL segment.</param>
    /// <param name="description">The brand story.</param>
    /// <param name="logoFileId">The logo.</param>
    /// <param name="seo">Crawler metadata.</param>
    public void Describe(string name, string slug, string? description, Guid? logoFileId, SeoMetadata seo)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Slug = Guard.NotNullOrWhiteSpace(slug);
        Description = description;
        LogoFileId = logoFileId;
        Seo = Guard.NotNull(seo);
    }

    /// <summary>Shows or hides the brand on the storefront.</summary>
    /// <param name="isActive">Whether it is offered.</param>
    public void SetActive(bool isActive) => IsActive = isActive;
}
