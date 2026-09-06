using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Content.Domain;

/// <summary>
/// What a navigation item points at.
/// </summary>
/// <remarks>
/// <para>
/// A discriminated target rather than a bare URL, and the difference is the difference between a
/// menu that survives a rename and one that does not. An item that points at a <em>category</em>
/// keeps working when merchandising changes that category's slug; one that points at the URL that
/// category happened to have is a 404 nobody notices until a shopper reports it.
/// </para>
/// <para>
/// <see cref="Url"/> is still here, because a menu always eventually needs to link to something
/// outside the store — a help centre, a campaign microsite — and refusing that would send editors
/// back to asking a developer.
/// </para>
/// </remarks>
internal enum MenuLinkType
{
    /// <summary>Nothing. A heading in a mega-menu column, or a parent whose children are the links.</summary>
    None = 0,

    /// <summary>A CMS page, by id. Follows the page's slug.</summary>
    Page = 10,

    /// <summary>A catalogue category, by id. Follows the category's slug.</summary>
    Category = 20,

    /// <summary>A curated collection, by id. Follows the collection's slug.</summary>
    Collection = 30,

    /// <summary>A literal path or absolute URL, exactly as typed.</summary>
    Url = 40,
}

/// <summary>
/// One item in a navigation menu (docs/03-database-design.md §4.14).
/// </summary>
/// <remarks>
/// Hierarchical by <see cref="ParentId"/> rather than by a materialised path, unlike the catalogue's
/// categories. A menu is two or three levels deep and is read whole, every time, by one query — the
/// path exists in the catalogue to answer "everything beneath this node" across a tree with thousands
/// of rows, and a menu never asks that question.
/// </remarks>
internal sealed class MenuItem : Entity<Guid>, ITenantScoped
{
    /// <summary>The longest label an item may carry.</summary>
    public const int MaxLabelLength = 120;

    /// <summary>The longest literal URL an item may carry.</summary>
    public const int MaxUrlLength = 2_048;

    /// <summary>How deep a menu may nest. Three levels is a mega-menu; four is a site map.</summary>
    public const int MaxDepth = 3;

    private MenuItem(Guid id, Guid menuId, string label, MenuLinkType linkType, int position)
        : base(id)
    {
        MenuId = menuId;
        Label = label;
        LinkType = linkType;
        Position = position;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private MenuItem() => Label = string.Empty;

    /// <summary>The menu this item belongs to.</summary>
    public Guid MenuId { get; private set; }

    /// <summary>The item above it, or null for a top-level item.</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>What a shopper reads.</summary>
    public string Label { get; private set; }

    /// <summary>What it points at.</summary>
    public MenuLinkType LinkType { get; private set; }

    /// <summary>The page, category or collection it points at. Null for the other two link types.</summary>
    public Guid? TargetId { get; private set; }

    /// <summary>The literal path or URL, for <see cref="MenuLinkType.Url"/>. Null otherwise.</summary>
    public string? Url { get; private set; }

    /// <summary>Where it sits among its siblings, ascending.</summary>
    public int Position { get; private set; }

    /// <summary>How deep it is. Zero for a top-level item.</summary>
    public int Depth { get; private set; }

    /// <summary>Whether it is rendered.</summary>
    public bool IsVisible { get; private set; } = true;

    /// <summary>Whether the link opens in a new tab. Meant for the external ones.</summary>
    public bool OpensInNewTab { get; private set; }

    /// <summary>An optional icon or thumbnail, for a mega-menu tile.</summary>
    public Guid? IconFileId { get; private set; }

    /// <summary>A short flash — "New", "Sale" — rendered beside the label.</summary>
    public string? Badge { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Adds an item to a menu.</summary>
    /// <param name="menuId">The menu.</param>
    /// <param name="parentId">The item above it, or null.</param>
    /// <param name="label">What a shopper reads.</param>
    /// <param name="linkType">What it points at.</param>
    /// <param name="targetId">The page, category or collection.</param>
    /// <param name="url">The literal URL, for a URL item.</param>
    /// <param name="position">Where it sits among its siblings.</param>
    /// <param name="depth">How deep it is.</param>
    public static MenuItem Create(
        Guid menuId,
        Guid? parentId,
        string label,
        MenuLinkType linkType,
        Guid? targetId,
        string? url,
        int position,
        int depth)
    {
        var item = new MenuItem(
            UuidV7.New(),
            Guard.NotEmpty(menuId),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(label), MaxLabelLength),
            linkType,
            position)
        {
            ParentId = parentId,
            TargetId = targetId,
            Url = url,
            Depth = depth,
        };

        return item;
    }

    /// <summary>Sets the presentation details an editor can change without moving the item.</summary>
    /// <param name="isVisible">Whether it is rendered.</param>
    /// <param name="opensInNewTab">Whether it opens in a new tab.</param>
    /// <param name="iconFileId">Its icon.</param>
    /// <param name="badge">Its badge.</param>
    public void Present(bool isVisible, bool opensInNewTab, Guid? iconFileId, string? badge)
    {
        IsVisible = isVisible;
        OpensInNewTab = opensInNewTab;
        IconFileId = iconFileId;
        Badge = badge;
    }
}

/// <summary>
/// A navigation menu (docs/03-database-design.md §4.14).
/// </summary>
/// <remarks>
/// <para>
/// Identified by a <see cref="Code"/> rather than an id, because the storefront asks for one by name
/// — <c>GET /store/content/menus/main</c> — and a layout that had to hold a UUID to render its own
/// header would break the moment the platform was deployed for a second business. The code is the
/// contract between this module and the Angular shell.
/// </para>
/// <para>
/// The footer builder the step card asks for is a menu, and deliberately not a separate concept: a
/// footer is columns of links, a column is a heading with children, and that is exactly the two-level
/// menu this aggregate already describes. Giving it its own table would be two implementations of
/// one idea, and the second one always ends up missing a feature.
/// </para>
/// </remarks>
internal sealed class Menu : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    /// <summary>The longest code a menu may carry.</summary>
    public const int MaxCodeLength = 64;

    /// <summary>The longest name.</summary>
    public const int MaxNameLength = 160;

    /// <summary>The most items one menu may hold, across every level.</summary>
    public const int MaxItems = 200;

    private readonly List<MenuItem> _items = [];

    private Menu(Guid id, string code, string name)
        : base(id)
    {
        Code = code;
        Name = name;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Menu()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>The stable key the storefront asks for. Unique per tenant.</summary>
    public string Code { get; private set; }

    /// <summary>What an editor calls it.</summary>
    public string Name { get; private set; }

    /// <summary>Where the storefront renders it — <c>header</c>, <c>footer</c>, <c>mobile</c>.</summary>
    public string? Placement { get; private set; }

    /// <summary>Whether the storefront serves it at all.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>The items, at every level.</summary>
    public IReadOnlyList<MenuItem> Items => _items;

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

    /// <summary>Opens a menu.</summary>
    /// <param name="code">Its stable key, already normalised and known to be free.</param>
    /// <param name="name">What an editor calls it.</param>
    /// <param name="placement">Where the storefront renders it.</param>
    public static Menu Create(string code, string name, string? placement)
        => new(
            UuidV7.New(),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(code), MaxCodeLength),
            Guard.MaxLength(Guard.NotNullOrWhiteSpace(name), MaxNameLength))
        {
            Placement = placement,
        };

    /// <summary>Renames the menu. The code is not editable — it is what the storefront asks for.</summary>
    /// <param name="name">Its new name.</param>
    /// <param name="placement">Where it renders.</param>
    /// <param name="isActive">Whether it is served.</param>
    public void Describe(string name, string? placement, bool isActive)
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name), MaxNameLength);
        Placement = placement;
        IsActive = isActive;
    }

    /// <summary>Replaces every item, wholesale.</summary>
    /// <remarks>
    /// The same reasoning as a page's blocks: a menu is edited as a tree in one screen and saved
    /// once, and reconstructing that as a sequence of moves is a lot of code that arrives at the
    /// state already sent.
    /// </remarks>
    /// <param name="items">The items, parents before their children.</param>
    public void ReplaceItems(IReadOnlyList<MenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items.Clear();
        _items.AddRange(items);
    }
}
