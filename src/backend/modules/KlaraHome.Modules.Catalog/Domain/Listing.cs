using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Catalog.Domain;

/// <summary>Where a vendor's offer is in its life.</summary>
internal enum ListingStatus
{
    /// <summary>Being set up by the seller. Not purchasable.</summary>
    Draft = 0,

    /// <summary>Live. Competes for the buy box.</summary>
    Active = 1,

    /// <summary>Paused by the seller, or withdrawn because they stopped trading.</summary>
    Inactive = 2,

    /// <summary>Retired for good. Terminal, because order lines point at it.</summary>
    Archived = 3,
}

/// <summary>
/// A vendor's offer to sell a variant at a price (docs/02-domain-model.md §3).
/// </summary>
/// <remarks>
/// <para>
/// This is the row that makes the platform a marketplace rather than a shop. Several sellers may
/// offer the same variant; each gets one listing, and the buy box picks between them. It is also
/// the id everything downstream holds: Inventory keys stock on it, a cart line points at it, and an
/// order line freezes a snapshot of it.
/// </para>
/// <para>
/// Price lives here and not on the variant, because price is what sellers compete on. The variant's
/// MRP is the statutory ceiling and belongs to the goods; the selling price is the offer and
/// belongs to the seller. The database enforces that the second never exceeds the first, because in
/// India that is not a preference.
/// </para>
/// </remarks>
internal sealed class Listing : AggregateRoot<Guid>, ITenantScoped, IAuditable, ISoftDeletable, IVendorScoped
{
    private Listing(Guid id, Guid vendorId, Guid variantId, Guid productId)
        : base(id)
    {
        VendorId = Guard.NotEmpty(vendorId);
        VariantId = Guard.NotEmpty(variantId);
        ProductId = Guard.NotEmpty(productId);
        Status = ListingStatus.Draft;
        Mrp = Money.Rupees(0m);
        SellingPrice = Money.Rupees(0m);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Listing()
    {
        Mrp = Money.Rupees(0m);
        SellingPrice = Money.Rupees(0m);
    }

    /// <summary>
    /// The seller making the offer. Never null on this table, unlike the nullable column
    /// <see cref="IVendorScoped"/> allows: an offer without a seller is not an offer.
    /// </summary>
    public Guid? VendorId { get; private set; }

    /// <summary>The variant being offered.</summary>
    public Guid VariantId { get; private set; }

    /// <summary>
    /// The variant's product, denormalised onto the offer.
    /// </summary>
    /// <remarks>
    /// Redundant, and worth it. Every admin listing screen filters or groups by product, and
    /// without this column each of them joins <c>variants</c> for a value that can never change —
    /// a variant does not move between products.
    /// </remarks>
    public Guid ProductId { get; private set; }

    /// <summary>Where the offer is in its life.</summary>
    public ListingStatus Status { get; private set; }

    /// <summary>Why it was last paused. Shown to the seller.</summary>
    public string? StatusReason { get; private set; }

    /// <summary>The MRP this seller declares. Never below the selling price.</summary>
    public Money Mrp { get; private set; }

    /// <summary>What the seller is asking, inclusive of GST.</summary>
    public Money SellingPrice { get; private set; }

    /// <summary>The seller's own code for the goods, printed on their pick list.</summary>
    public string? VendorSku { get; private set; }

    /// <summary>How long this seller needs before handing the parcel to a courier.</summary>
    public int HandlingTimeHours { get; private set; } = DefaultHandlingTimeHours;

    /// <summary>Whether this seller accepts cash on delivery for this offer.</summary>
    public bool IsCodAllowed { get; private set; } = true;

    /// <summary>The most units one order may take, or null for no cap of its own.</summary>
    public int? MaxOrderQuantity { get; private set; }

    /// <summary>When it last went live. Used as the final, stable buy-box tie-break.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

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

    /// <summary>Whether the offer itself is live. Its variant, product and seller must be too.</summary>
    public bool IsLive => Status == ListingStatus.Active;

    /// <summary>What a seller is given until they say otherwise: one working day.</summary>
    public const int DefaultHandlingTimeHours = 24;

    /// <summary>The longest handling time the platform will publish to a shopper.</summary>
    public const int MaxHandlingTimeHours = 168;

    /// <summary>Opens an offer, in draft.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="variantId">The variant being offered.</param>
    /// <param name="productId">Its product.</param>
    public static Listing Open(Guid vendorId, Guid variantId, Guid productId)
        => new(UuidV7.New(), vendorId, variantId, productId);

    /// <summary>Whether the life cycle allows one status to follow another.</summary>
    /// <param name="from">The current status.</param>
    /// <param name="to">The status being moved to.</param>
    public static bool IsTransitionAllowed(ListingStatus from, ListingStatus to)
        => (from, to) switch
        {
            (ListingStatus.Draft, ListingStatus.Active) => true,
            (ListingStatus.Active, ListingStatus.Inactive) => true,
            (ListingStatus.Inactive, ListingStatus.Active) => true,
            (_, ListingStatus.Archived) => from != ListingStatus.Archived,
            _ => false,
        };

    /// <summary>Moves the offer, or returns false if the life cycle does not allow it.</summary>
    /// <param name="next">The status to move to.</param>
    /// <param name="at">When the transition happened.</param>
    /// <param name="reason">Why, for a pause.</param>
    public bool TransitionTo(ListingStatus next, DateTimeOffset at, string? reason = null)
    {
        if (!IsTransitionAllowed(Status, next))
        {
            return false;
        }

        Status = next;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        if (next == ListingStatus.Active)
        {
            PublishedAt ??= at;
        }

        return true;
    }

    /// <summary>Sets the commercial terms of the offer.</summary>
    /// <param name="mrp">The declared MRP.</param>
    /// <param name="sellingPrice">What the seller is asking.</param>
    /// <param name="vendorSku">The seller's own code.</param>
    /// <param name="handlingTimeHours">How long they need before dispatch.</param>
    /// <param name="isCodAllowed">Whether they accept cash on delivery.</param>
    /// <param name="maxOrderQuantity">The most units one order may take.</param>
    public void SetTerms(
        Money mrp,
        Money sellingPrice,
        string? vendorSku,
        int handlingTimeHours,
        bool isCodAllowed,
        int? maxOrderQuantity)
    {
        Mrp = mrp;
        SellingPrice = sellingPrice;
        VendorSku = string.IsNullOrWhiteSpace(vendorSku) ? null : vendorSku.Trim();
        HandlingTimeHours = Math.Clamp(handlingTimeHours, 1, MaxHandlingTimeHours);
        IsCodAllowed = isCodAllowed;
        MaxOrderQuantity = maxOrderQuantity is > 0 ? maxOrderQuantity : null;
    }

    /// <summary>Retires the offer. Order lines point at it, so it is never actually removed.</summary>
    /// <param name="at">When.</param>
    public void Delete(DateTimeOffset at)
    {
        DeletedAt = at;
        Status = ListingStatus.Archived;
    }
}
