using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Catalog.Domain;

/// <summary>What kind of asset a gallery entry points at.</summary>
internal enum CatalogMediaKind
{
    /// <summary>A photograph or render. The overwhelming majority.</summary>
    Image = 0,

    /// <summary>A product video, hosted in the media library like everything else.</summary>
    Video = 1,

    /// <summary>A manual, a datasheet or a certificate, offered as a download.</summary>
    Document = 2,
}

/// <summary>
/// One entry in a product's or a variant's gallery (docs/03-database-design.md §4.4).
/// </summary>
/// <remarks>
/// <para>
/// One table for both, discriminated by which owner column is set, rather than the two tables §4.4
/// sketches. They have identical columns and identical behaviour, and splitting them would double
/// every query the PDP makes — it needs the union, ordered, in one read.
/// </para>
/// <para>
/// <see cref="FileId"/> is a <c>media.files</c> id and deliberately not a foreign key: no key may
/// cross a schema (docs/01-architecture.md §2.1). It is resolved through <c>IMediaLibrary</c>, and
/// an id that resolves to nothing renders as a gap rather than failing the page.
/// </para>
/// </remarks>
internal sealed class CatalogMediaAsset : Entity<Guid>, ITenantScoped, IAuditable
{
    private CatalogMediaAsset(Guid id, Guid fileId, CatalogMediaKind kind)
        : base(id)
    {
        FileId = Guard.NotEmpty(fileId);
        Kind = kind;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private CatalogMediaAsset()
    {
    }

    /// <summary>The product this belongs to, or null when it belongs to a variant.</summary>
    public Guid? ProductId { get; private set; }

    /// <summary>The variant this belongs to, or null when it belongs to a product.</summary>
    public Guid? VariantId { get; private set; }

    /// <summary>The stored file, as a <c>media.files</c> id. A soft reference.</summary>
    public Guid FileId { get; private set; }

    /// <summary>What kind of asset it is.</summary>
    public CatalogMediaKind Kind { get; private set; }

    /// <summary>The alt text. Not optional in practice — an image without it fails accessibility.</summary>
    public string? AltText { get; private set; }

    /// <summary>Sort order within the gallery. Position zero is the primary image.</summary>
    public int Position { get; private set; }

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

    /// <summary>Attaches an asset to a product's gallery.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="fileId">The stored file.</param>
    /// <param name="kind">What kind of asset it is.</param>
    /// <param name="altText">The alt text.</param>
    /// <param name="position">Sort order; zero is primary.</param>
    public static CatalogMediaAsset ForProduct(
        Guid productId,
        Guid fileId,
        CatalogMediaKind kind,
        string? altText,
        int position)
        => new(UuidV7.New(), fileId, kind)
        {
            ProductId = Guard.NotEmpty(productId),
            AltText = altText,
            Position = Math.Max(position, 0),
        };

    /// <summary>Attaches an asset to a variant's gallery.</summary>
    /// <param name="variantId">The variant.</param>
    /// <param name="productId">Its product, so a PDP read needs one index and not two.</param>
    /// <param name="fileId">The stored file.</param>
    /// <param name="kind">What kind of asset it is.</param>
    /// <param name="altText">The alt text.</param>
    /// <param name="position">Sort order; zero is primary.</param>
    public static CatalogMediaAsset ForVariant(
        Guid variantId,
        Guid productId,
        Guid fileId,
        CatalogMediaKind kind,
        string? altText,
        int position)
        => new(UuidV7.New(), fileId, kind)
        {
            VariantId = Guard.NotEmpty(variantId),
            ProductId = Guard.NotEmpty(productId),
            AltText = altText,
            Position = Math.Max(position, 0),
        };

    /// <summary>Re-labels or re-orders the entry.</summary>
    /// <param name="altText">The alt text.</param>
    /// <param name="position">Sort order.</param>
    public void Describe(string? altText, int position)
    {
        AltText = altText;
        Position = Math.Max(position, 0);
    }
}
