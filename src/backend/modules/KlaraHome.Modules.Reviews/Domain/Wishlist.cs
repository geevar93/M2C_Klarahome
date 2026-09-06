using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Reviews.Domain;

/// <summary>
/// A saved list of things somebody wants (docs/03-database-design.md §4.15).
/// </summary>
/// <remarks>
/// <para>
/// Every customer has exactly one list called <see cref="DefaultName"/>, created the first time they
/// save anything, and may make more. The default is what "save for later" writes to and is what the
/// heart icon toggles; the named ones are what a shopper builds a room or a gift list in.
/// </para>
/// <para>
/// It belongs to a signed-in customer and to nobody else. There is deliberately no anonymous
/// wishlist: a basket survives a sign-in because it merges (Step 13), and a list of intentions
/// stored against a cookie is a list the shopper loses on their next device and a store cannot use
/// for anything at all.
/// </para>
/// <para>
/// <see cref="ShareToken"/> is what makes a gift list work. It is an opaque random value rather than
/// the list's id, so possessing a wishlist id — which the owner's own browser has — grants nothing,
/// and revoking a share is one column being nulled rather than a list being rebuilt.
/// </para>
/// </remarks>
internal sealed class Wishlist : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    /// <summary>What the list every customer starts with is called.</summary>
    public const string DefaultName = "Saved items";

    /// <summary>The longest name a shopper may give a list.</summary>
    public const int MaxNameLength = 80;

    /// <summary>The most items one list will hold.</summary>
    /// <remarks>
    /// A ceiling rather than a product decision. Without one, a script can grow a single row set
    /// without bound against an endpoint that is authenticated but otherwise cheap to call.
    /// </remarks>
    public const int MaxItems = 500;

    private readonly List<WishlistItem> _items = [];

    private Wishlist(Guid id, Guid customerId, string name, bool isDefault)
        : base(id)
    {
        CustomerId = customerId;
        Name = name;
        IsDefault = isDefault;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Wishlist() => Name = string.Empty;

    /// <summary>Whose list it is.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>What they call it.</summary>
    public string Name { get; private set; }

    /// <summary>Whether it is the list "save for later" writes to. Exactly one per customer.</summary>
    public bool IsDefault { get; private set; }

    /// <summary>The token that lets somebody else see it, or null when it is private.</summary>
    public string? ShareToken { get; private set; }

    /// <summary>How many items are on it. A cache, so a list page needs no count.</summary>
    public int ItemCount { get; private set; }

    /// <summary>What is on it.</summary>
    public IReadOnlyList<WishlistItem> Items => _items;

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

    /// <summary>Opens a list.</summary>
    /// <param name="customerId">Whose it is.</param>
    /// <param name="name">What to call it, or null for the default name.</param>
    /// <param name="isDefault">Whether this is the customer's default list.</param>
    public static Wishlist Open(Guid customerId, string? name, bool isDefault)
        => new(
            UuidV7.New(),
            Guard.NotEmpty(customerId),
            string.IsNullOrWhiteSpace(name) ? DefaultName : Guard.MaxLength(name.Trim(), MaxNameLength),
            isDefault);

    /// <summary>Renames it. The default list may be renamed; it stays the default.</summary>
    /// <param name="name">The new name.</param>
    public void Rename(string name)
        => Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name).Trim(), MaxNameLength);

    /// <summary>Adds an item, or returns the one already there.</summary>
    /// <remarks>
    /// Idempotent on the variant. Tapping a heart twice is how a shopper removes something, and the
    /// endpoint calls <see cref="Remove"/> for that; a second add arriving from a retried request
    /// must not produce a second row.
    /// </remarks>
    /// <param name="variantId">The sellable thing being saved.</param>
    /// <param name="productId">Its product, for the card the list renders.</param>
    /// <param name="note">What the shopper wrote against it.</param>
    /// <param name="priority">Where it sits in the list.</param>
    public WishlistItem Add(Guid variantId, Guid productId, string? note, int priority)
    {
        var existing = _items.Find(item => item.VariantId == variantId);

        if (existing is not null)
        {
            existing.Annotate(note, priority);
            return existing;
        }

        var added = WishlistItem.For(Id, variantId, productId, note, priority);

        _items.Add(added);
        ItemCount = _items.Count;

        return added;
    }

    /// <summary>Takes an item off. Returns whether anything was there.</summary>
    /// <param name="variantId">The sellable thing being removed.</param>
    public bool Remove(Guid variantId)
    {
        var removed = _items.RemoveAll(item => item.VariantId == variantId) > 0;

        if (removed)
        {
            ItemCount = _items.Count;
        }

        return removed;
    }

    /// <summary>Turns sharing on, and returns the token.</summary>
    /// <param name="token">The opaque token, minted by the caller from a cryptographic source.</param>
    public string Share(string token)
    {
        ShareToken = Guard.NotNullOrWhiteSpace(token);
        return ShareToken;
    }

    /// <summary>Turns sharing off. Any link already handed out stops working.</summary>
    public void Unshare() => ShareToken = null;

    /// <summary>Recounts the items, for a list loaded without them.</summary>
    /// <param name="count">How many there are.</param>
    public void RecordItemCount(int count) => ItemCount = Math.Max(0, count);
}

/// <summary>
/// One thing on a wishlist (docs/03-database-design.md §4.15).
/// </summary>
/// <remarks>
/// <para>
/// Keyed on the variant, because that is what a shopper saves: somebody who wants the beige one in
/// large does not want to be shown the grey one when they come back. The product is carried
/// alongside so the card can be rendered and so "which of my saved things is this" can be answered
/// on a product page without resolving every variant.
/// </para>
/// <para>
/// It stores no price. A saved item's price is whatever the buy box says today, resolved at read
/// time through the catalogue's contracts — which is what makes a wishlist a live shopping surface
/// rather than a snapshot that quotes last month's figure. A shopper who wants to be told when the
/// price moves has a stock subscription, which is a different row and a different intention.
/// </para>
/// </remarks>
internal sealed class WishlistItem : Entity<Guid>, ITenantScoped, IAuditable
{
    /// <summary>The longest note a shopper may write against an item.</summary>
    public const int MaxNoteLength = 250;

    private WishlistItem(Guid id, Guid wishlistId, Guid variantId, Guid productId)
        : base(id)
    {
        WishlistId = wishlistId;
        VariantId = variantId;
        ProductId = productId;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private WishlistItem()
    {
    }

    /// <summary>The list it is on.</summary>
    public Guid WishlistId { get; private set; }

    /// <summary>The sellable thing saved.</summary>
    public Guid VariantId { get; private set; }

    /// <summary>Its product.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>What the shopper wrote against it.</summary>
    public string? Note { get; private set; }

    /// <summary>Where it sits in the list. Lower first.</summary>
    public int Priority { get; private set; }

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

    /// <summary>Puts something on a list.</summary>
    /// <param name="wishlistId">The list.</param>
    /// <param name="variantId">The sellable thing.</param>
    /// <param name="productId">Its product.</param>
    /// <param name="note">What the shopper wrote.</param>
    /// <param name="priority">Where it sits.</param>
    public static WishlistItem For(Guid wishlistId, Guid variantId, Guid productId, string? note, int priority)
        => new(UuidV7.New(), Guard.NotEmpty(wishlistId), Guard.NotEmpty(variantId), Guard.NotEmpty(productId))
        {
            Note = Clean(note),
            Priority = Math.Max(0, priority),
        };

    /// <summary>Changes the note and the position.</summary>
    /// <param name="note">What the shopper wrote.</param>
    /// <param name="priority">Where it sits.</param>
    public void Annotate(string? note, int priority)
    {
        Note = Clean(note);
        Priority = Math.Max(0, priority);
    }

    private static string? Clean(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();

        return trimmed.Length <= MaxNoteLength ? trimmed : trimmed[..MaxNoteLength];
    }
}
