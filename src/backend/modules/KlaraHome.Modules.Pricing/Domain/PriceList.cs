using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Pricing.Domain;

/// <summary>What a price list is for. Presentational; <see cref="PriceList.Priority"/> is what ranks.</summary>
/// <remarks>
/// The type is deliberately not a rank. A marketplace that ranked on it would have no way to say
/// "this sale beats that sale", and the first Diwali campaign that overlapped a clearance would be
/// decided by whichever row the query happened to read first.
/// </remarks>
internal enum PriceListType
{
    /// <summary>The standing price. Open-ended, and usually the only list a small store has.</summary>
    Base = 0,

    /// <summary>A markdown that runs until somebody turns it off.</summary>
    Sale = 1,

    /// <summary>A markdown with a window it opens and closes on by itself.</summary>
    Scheduled = 2,
}

/// <summary>
/// A named set of prices that applies for a window, to a seller, in an order
/// (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// The resolution rule is the whole of this aggregate's meaning: among the lists that are active,
/// in window, and either platform-wide or the offer's own seller's, the lowest
/// <see cref="Priority"/> wins, and within the winning list the highest quantity tier at or below
/// the requested quantity wins. It is stated once, here, because a price a shopper cannot account
/// for is a support call and a price the platform cannot account for is a refund.
/// </para>
/// <para>
/// An offer with no item in any applicable list keeps the price on its own listing. That fallback
/// is what lets a deployment run with no price lists at all, which is how most of them will start.
/// </para>
/// <para>
/// <see cref="IPlatformShared"/> is what makes the platform's own lists visible to a seller, and it
/// is not optional here: a platform-wide list prices <em>their</em> offers, so a seller who could
/// not read it could not be told why their offer is selling at a figure they did not set. Widening
/// the read says nothing about the write — that a seller may not edit a platform list is
/// <c>PricingScope.CanWrite</c>'s rule, and it only becomes reachable once the row is readable.
/// </para>
/// </remarks>
internal sealed class PriceList
    : AggregateRoot<Guid>, ITenantScoped, IAuditable, IVendorScoped, IPlatformShared
{
    private readonly List<PriceListItem> _items = [];

    private PriceList(Guid id, Guid? vendorId, string code, string name, PriceListType type, string currencyCode)
        : base(id)
    {
        VendorId = vendorId;
        Code = Guard.NotNullOrWhiteSpace(code);
        Name = Guard.NotNullOrWhiteSpace(name);
        Type = type;
        CurrencyCode = Guard.NotNullOrWhiteSpace(currencyCode).ToUpperInvariant();
        Priority = DefaultPriority;
        IsActive = true;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private PriceList()
    {
        Code = string.Empty;
        Name = string.Empty;
        CurrencyCode = Money.Inr;
    }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>The short code an operator quotes. Unique within the tenant.</summary>
    public string Code { get; private set; }

    /// <summary>What it is called on a screen.</summary>
    public string Name { get; private set; }

    /// <summary>Why it exists. Presentational.</summary>
    public PriceListType Type { get; private set; }

    /// <summary>ISO 4217 code every item in it is priced in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>Resolution order. Lower wins.</summary>
    public int Priority { get; private set; }

    /// <summary>When it starts applying, or null for "already".</summary>
    public DateTimeOffset? StartsAt { get; private set; }

    /// <summary>When it stops applying, or null for "never".</summary>
    public DateTimeOffset? EndsAt { get; private set; }

    /// <summary>Whether it is considered at all. An operator's switch, independent of the window.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The prices in it.</summary>
    public IReadOnlyCollection<PriceListItem> Items => _items;

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

    /// <summary>The middle of the range, so a new list can be ordered either side of an existing one.</summary>
    public const int DefaultPriority = 100;

    /// <summary>The highest priority number this platform stores.</summary>
    public const int MaxPriority = 1000;

    /// <summary>Opens a price list.</summary>
    /// <param name="vendorId">The seller it prices for, or null for a platform-wide list.</param>
    /// <param name="code">The short code an operator quotes.</param>
    /// <param name="name">What it is called.</param>
    /// <param name="type">Why it exists.</param>
    /// <param name="currencyCode">ISO 4217 code its items are priced in.</param>
    public static PriceList Create(
        Guid? vendorId,
        string code,
        string name,
        PriceListType type,
        string currencyCode)
        => new(UuidV7.New(), vendorId, code.Trim().ToUpperInvariant(), name, type, currencyCode);

    /// <summary>Restates the list's name, rank and window.</summary>
    /// <param name="name">What it is called.</param>
    /// <param name="type">Why it exists.</param>
    /// <param name="priority">Resolution order. Lower wins.</param>
    /// <param name="startsAt">When it starts applying.</param>
    /// <param name="endsAt">When it stops.</param>
    public void Update(string name, PriceListType type, int priority, DateTimeOffset? startsAt, DateTimeOffset? endsAt)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Type = type;
        Priority = Math.Clamp(priority, 0, MaxPriority);
        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    /// <summary>Turns the list on or off, regardless of its window.</summary>
    /// <param name="isActive">Whether it is considered.</param>
    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>Whether the list applies at <paramref name="asOf"/>.</summary>
    /// <remarks>
    /// Both halves matter and neither implies the other: a scheduled list is active long before it
    /// starts, and an expired list is still active until somebody turns it off.
    /// </remarks>
    /// <param name="asOf">The instant to test.</param>
    public bool AppliesAt(DateTimeOffset asOf)
        => IsActive && (StartsAt is null || StartsAt <= asOf) && (EndsAt is null || EndsAt > asOf);

    /// <summary>Whether the list may price an offer sold by <paramref name="vendorId"/>.</summary>
    /// <param name="vendorId">The seller behind the offer.</param>
    public bool AppliesTo(Guid vendorId) => VendorId is null || VendorId == vendorId;

    /// <summary>Adds or restates one price, keyed on the offer and the quantity tier.</summary>
    /// <param name="listingId">The offer.</param>
    /// <param name="price">What one unit costs at this tier, inclusive of GST.</param>
    /// <param name="minQuantity">The smallest quantity this tier applies from.</param>
    /// <returns>The item, whether it was created or restated.</returns>
    public PriceListItem SetPrice(Guid listingId, Money price, int minQuantity)
    {
        var tier = Math.Max(1, minQuantity);
        var existing = _items.Find(item => item.ListingId == listingId && item.MinQuantity == tier);

        if (existing is not null)
        {
            existing.SetPrice(price);
            return existing;
        }

        var added = PriceListItem.Create(Id, listingId, price, tier);
        _items.Add(added);

        return added;
    }

    /// <summary>Removes one price. Returns false when there was nothing to remove.</summary>
    /// <param name="itemId">The item.</param>
    public bool RemoveItem(Guid itemId)
    {
        var item = _items.Find(candidate => candidate.Id == itemId);

        return item is not null && _items.Remove(item);
    }
}

/// <summary>
/// One offer's price in one list, at one quantity tier (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// Tiered quantity pricing is several of these differing only in <see cref="MinQuantity"/>. There
/// is deliberately no MRP here: MRP is statutory and belongs to the product, and a second copy of
/// it would be a second answer on an invoice.
/// </remarks>
internal sealed class PriceListItem : Entity<Guid>, ITenantScoped, IAuditable
{
    private PriceListItem(Guid id, Guid priceListId, Guid listingId, Money price, int minQuantity)
        : base(id)
    {
        PriceListId = priceListId;
        ListingId = listingId;
        Price = price;
        MinQuantity = minQuantity;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private PriceListItem()
    {
    }

    /// <summary>The list it belongs to.</summary>
    public Guid PriceListId { get; private set; }

    /// <summary>The offer it prices. A plain id: no foreign key crosses a schema.</summary>
    public Guid ListingId { get; private set; }

    /// <summary>What one unit costs at this tier, inclusive of GST.</summary>
    public Money Price { get; private set; }

    /// <summary>The smallest quantity this tier applies from. One for the ordinary case.</summary>
    public int MinQuantity { get; private set; }

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

    /// <summary>Prices an offer at a tier.</summary>
    /// <param name="priceListId">The list.</param>
    /// <param name="listingId">The offer.</param>
    /// <param name="price">What one unit costs.</param>
    /// <param name="minQuantity">The smallest quantity it applies from.</param>
    public static PriceListItem Create(Guid priceListId, Guid listingId, Money price, int minQuantity)
        => new(UuidV7.New(), priceListId, listingId, price, Math.Max(1, minQuantity));

    /// <summary>Restates the price.</summary>
    /// <param name="price">What one unit now costs.</param>
    public void SetPrice(Money price) => Price = price;
}
