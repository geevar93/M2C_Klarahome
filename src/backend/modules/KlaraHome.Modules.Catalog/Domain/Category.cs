using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Catalog.Domain;

/// <summary>
/// A node in the browse tree (docs/03-database-design.md §4.4).
/// </summary>
/// <remarks>
/// <para>
/// The tree is stored as a <b>materialised path</b> rather than walked through <c>parent_id</c>:
/// "everything under Home &amp; Kitchen" is the most common catalogue query there is, and with an
/// adjacency list alone it is a recursive CTE on every product listing page. With a path it is one
/// index range scan.
/// </para>
/// <para>
/// The path is built from <em>ids</em>, not slugs. A slug is renamed the day marketing changes its
/// mind, and a slug-based path would then have to be rewritten across every descendant while the
/// storefront is reading it. An id never changes, so a subtree's paths are rewritten only when the
/// subtree is genuinely moved.
/// </para>
/// </remarks>
internal sealed class Category : AggregateRoot<Guid>, ITenantScoped, IAuditable, ISoftDeletable
{
    private Category(Guid id, string name, string slug)
        : base(id)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Slug = Guard.NotNullOrWhiteSpace(slug);
        Path = string.Empty;
        Seo = new SeoMetadata();
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Category()
    {
        Name = string.Empty;
        Slug = string.Empty;
        Path = string.Empty;
        Seo = new SeoMetadata();
    }

    /// <summary>The parent node, or null for a root.</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>What the category is called.</summary>
    public string Name { get; private set; }

    /// <summary>The URL path segment. Unique per tenant, because the storefront routes on it.</summary>
    public string Slug { get; private set; }

    /// <summary>Copy shown at the top of the category page.</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// The materialised path: every ancestor's id and then this node's, each wrapped in slashes.
    /// A descendant's path always starts with its ancestor's.
    /// </summary>
    public string Path { get; private set; }

    /// <summary>Depth in the tree. Zero for a root.</summary>
    public int Level { get; private set; }

    /// <summary>Sort order among siblings. Lower first.</summary>
    public int Position { get; private set; }

    /// <summary>Whether the category is shown on the storefront.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>The tile image, as a <c>media.files</c> id. A soft reference, never a foreign key.</summary>
    public Guid? ImageFileId { get; private set; }

    /// <summary>The attribute set products in this category are described with, if one is imposed.</summary>
    public Guid? AttributeSetId { get; private set; }

    /// <summary>What a crawler is told about the category page.</summary>
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

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <inheritdoc />
    public Guid? DeletedBy { get; private set; }

    /// <summary>The deepest tree this platform will build. Beyond it a breadcrumb stops being read.</summary>
    public const int MaxDepth = 6;

    /// <summary>The path separator. Also the character a slug may never contain.</summary>
    public const char PathSeparator = '/';

    /// <summary>Creates a node under an optional parent.</summary>
    /// <param name="name">What it is called.</param>
    /// <param name="slug">The URL segment, already normalised and known to be free.</param>
    /// <param name="parent">The parent node, or null for a root.</param>
    public static Category Create(string name, string slug, Category? parent)
    {
        var category = new Category(UuidV7.New(), name, slug);
        category.Reparent(parent);

        return category;
    }

    /// <summary>
    /// Recomputes this node's path and level from its parent, and returns the path it had before.
    /// </summary>
    /// <remarks>
    /// Deliberately returns rather than cascades. A subtree move touches rows this aggregate does
    /// not own, and an entity that quietly loaded and rewrote a hundred others would be an
    /// aggregate boundary in name only. The caller rebases the descendants with the returned
    /// prefix.
    /// </remarks>
    /// <param name="parent">The new parent, or null to make this a root.</param>
    public string Reparent(Category? parent)
    {
        var previous = Path;

        ParentId = parent?.Id;
        Level = parent is null ? 0 : parent.Level + 1;
        Path = parent is null
            ? string.Concat(PathSeparator, Id.ToString(), PathSeparator)
            : string.Concat(parent.Path, Id.ToString(), PathSeparator);

        return previous;
    }

    /// <summary>Rewrites the ancestor part of this node's path after its subtree was moved.</summary>
    /// <param name="oldPrefix">The moved node's former path.</param>
    /// <param name="newPrefix">Its new path.</param>
    /// <param name="levelShift">How many levels the moved node went down (or up, if negative).</param>
    public void RebaseUnder(string oldPrefix, string newPrefix, int levelShift)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldPrefix);
        ArgumentException.ThrowIfNullOrEmpty(newPrefix);

        if (!Path.StartsWith(oldPrefix, StringComparison.Ordinal))
        {
            return;
        }

        Path = string.Concat(newPrefix, Path.AsSpan(oldPrefix.Length));
        Level += levelShift;
    }

    /// <summary>Whether moving this node under <paramref name="candidate"/> would create a cycle.</summary>
    /// <remarks>
    /// A node cannot become its own descendant's child, and the materialised path turns that from a
    /// walk up the tree into a prefix test.
    /// </remarks>
    /// <param name="candidate">The proposed new parent.</param>
    public bool WouldCycleUnder(Category candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return candidate.Id == Id || candidate.Path.StartsWith(Path, StringComparison.Ordinal);
    }

    /// <summary>Renames the node and updates what a crawler is told about it.</summary>
    /// <param name="name">What it is called.</param>
    /// <param name="slug">The URL segment.</param>
    /// <param name="description">Copy for the category page.</param>
    /// <param name="imageFileId">The tile image.</param>
    /// <param name="attributeSetId">The attribute set products here are described with.</param>
    /// <param name="seo">Crawler metadata.</param>
    public void Describe(
        string name,
        string slug,
        string? description,
        Guid? imageFileId,
        Guid? attributeSetId,
        SeoMetadata seo)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Slug = Guard.NotNullOrWhiteSpace(slug);
        Description = description;
        ImageFileId = imageFileId;
        AttributeSetId = attributeSetId;
        Seo = Guard.NotNull(seo);
    }

    /// <summary>Sets the sort order among siblings.</summary>
    /// <param name="position">Lower sorts first.</param>
    public void MoveTo(int position) => Position = Math.Max(position, 0);

    /// <summary>Shows or hides the category on the storefront.</summary>
    /// <param name="isActive">Whether it is shown.</param>
    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>Retires the node. History matters here, so it is never actually removed.</summary>
    /// <param name="at">When.</param>
    public void Delete(DateTimeOffset at)
    {
        DeletedAt = at;
        IsActive = false;
    }
}
