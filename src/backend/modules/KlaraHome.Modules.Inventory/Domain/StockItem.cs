using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Inventory.Domain;

/// <summary>How closely the individual units of a stock item are tracked.</summary>
/// <remarks>
/// The design is present for every item; enforcement is per category rather than platform-wide,
/// which is what the Step 11 card means by "enforcement optional". A homeware marketplace needs
/// batches for anything with an expiry and serials for anything with a warranty, and needs neither
/// for a cushion cover.
/// </remarks>
internal enum StockTrackingMode
{
    /// <summary>A count and nothing else. The default, and right for most goods.</summary>
    None = 0,

    /// <summary>Units are grouped into lots with a manufacture and expiry date.</summary>
    Batch = 1,

    /// <summary>Every unit is individually identified.</summary>
    Serial = 2,
}

/// <summary>
/// The stock of one offer at one location — the aggregate root of this module
/// (docs/02-domain-model.md §4.2).
/// </summary>
/// <remarks>
/// <para>
/// Both quantity properties are <b>derived caches</b>. The ledger is the record of truth, and the
/// invariant is <c>quantity_on_hand = Σ(ledger.change)</c> with
/// <c>quantity_reserved = Σ(ledger.reserved_change)</c>; a nightly job asserts both. They are
/// cached because "how many are there" is asked on every product page and summing an append-only
/// table to answer it would not survive a catalogue of any size.
/// </para>
/// <para>
/// Nothing here mutates a quantity. Every movement goes through the ledger service, which applies
/// it as a single conditional <c>UPDATE</c> so that two callers racing for the last unit produce
/// one success and one refusal rather than two successes — the check and the write cannot be
/// separated by anything, which is precisely what a read-modify-write in this class would do.
/// </para>
/// </remarks>
internal sealed class StockItem : AggregateRoot<Guid>, ITenantScoped, IAuditable, IVendorScoped
{
    private StockItem(Guid id, Guid listingId, Guid warehouseId, Guid? vendorId, string sku)
        : base(id)
    {
        ListingId = Guard.NotEmpty(listingId);
        WarehouseId = Guard.NotEmpty(warehouseId);
        VendorId = vendorId;
        Sku = Guard.NotNullOrWhiteSpace(sku);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private StockItem() => Sku = string.Empty;

    /// <summary>The offer this stock is for. A <c>catalog.listings</c> id, held as a plain UUID.</summary>
    public Guid ListingId { get; private set; }

    /// <summary>Where it is held.</summary>
    public Guid WarehouseId { get; private set; }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>
    /// The variant's SKU, denormalised from Catalog.
    /// </summary>
    /// <remarks>
    /// Copied rather than joined, because no query may cross a schema boundary and every stock
    /// screen, pick list and low-stock alert has to say which goods it is talking about. It is
    /// refreshed when the listing changes, and it is a label, never a key.
    /// </remarks>
    public string Sku { get; private set; }

    /// <summary>Units physically held. A cache of the ledger sum.</summary>
    public int QuantityOnHand { get; private set; }

    /// <summary>Units held for a cart or an order. A cache of the ledger sum.</summary>
    public int QuantityReserved { get; private set; }

    /// <summary>The level at or below which the seller wants to be told.</summary>
    public int ReorderLevel { get; private set; }

    /// <summary>How many the seller buys at a time, printed on the alert.</summary>
    public int ReorderQuantity { get; private set; }

    /// <summary>Whether the seller accepts orders beyond what is on hand.</summary>
    public bool AllowBackorder { get; private set; }

    /// <summary>Whether the offer may be sold before it is released.</summary>
    public bool AllowPreorder { get; private set; }

    /// <summary>When a pre-ordered unit is expected to ship.</summary>
    public DateTimeOffset? PreorderAvailableAt { get; private set; }

    /// <summary>How closely individual units are tracked.</summary>
    public StockTrackingMode TrackingMode { get; private set; }

    /// <summary>
    /// When this item last raised a low-stock alert, cleared once it is replenished above the
    /// level. This is what makes the alert fire on the crossing rather than on every sale below it.
    /// </summary>
    public DateTimeOffset? LowStockNotifiedAt { get; private set; }

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

    /// <summary>What may still be sold: on hand less reserved, floored at zero.</summary>
    /// <remarks>
    /// Floored because a backorder-enabled item may legitimately hold more than it has, and a
    /// negative "available" is not a number any caller wants to reason about.
    /// </remarks>
    public int QuantityAvailable => Math.Max(0, QuantityOnHand - QuantityReserved);

    /// <summary>Whether anything can be sold right now, backorder and pre-order included.</summary>
    public bool IsAvailable => AllowBackorder || AllowPreorder || QuantityAvailable > 0;

    /// <summary>Whether the item is at or below the level its seller asked to be told about.</summary>
    /// <remarks>
    /// A reorder level of zero means "do not alert" rather than "alert when empty". A seller who
    /// wants to hear about an empty shelf sets the level to one, which is a statement they made
    /// rather than a default that would alert on every item nobody has configured.
    /// </remarks>
    public bool IsLow => ReorderLevel > 0 && QuantityAvailable <= ReorderLevel;

    /// <summary>Opens the stock row for an offer at a location, at zero.</summary>
    /// <param name="listingId">The offer.</param>
    /// <param name="warehouseId">Where it is held.</param>
    /// <param name="vendorId">The seller, copied from the listing so the vendor filter can work.</param>
    /// <param name="sku">The variant's SKU, for display.</param>
    public static StockItem Open(Guid listingId, Guid warehouseId, Guid? vendorId, string sku)
        => new(UuidV7.New(), listingId, warehouseId, vendorId, sku);

    /// <summary>Sets the replenishment and selling policy for this item.</summary>
    /// <param name="reorderLevel">The level at or below which to alert. Zero disables the alert.</param>
    /// <param name="reorderQuantity">How many the seller buys at a time.</param>
    /// <param name="allowBackorder">Whether to accept orders beyond what is on hand.</param>
    /// <param name="allowPreorder">Whether the offer may be sold before it is released.</param>
    /// <param name="preorderAvailableAt">When a pre-ordered unit is expected to ship.</param>
    /// <param name="trackingMode">How closely individual units are tracked.</param>
    public void Configure(
        int reorderLevel,
        int reorderQuantity,
        bool allowBackorder,
        bool allowPreorder,
        DateTimeOffset? preorderAvailableAt,
        StockTrackingMode trackingMode)
    {
        ReorderLevel = Math.Max(0, reorderLevel);
        ReorderQuantity = Math.Max(0, reorderQuantity);
        AllowBackorder = allowBackorder;
        AllowPreorder = allowPreorder;

        // A pre-order date on an item that is not on pre-order is a date nothing will ever read,
        // and one that outlives the flag is how a storefront promises a ship date for stock that
        // is already on the shelf.
        PreorderAvailableAt = allowPreorder ? preorderAvailableAt : null;
        TrackingMode = trackingMode;

        // The seller just moved the goalposts. Whether this item is low is now a different
        // question, so the previous answer must not suppress the next alert.
        if (!IsLow)
        {
            LowStockNotifiedAt = null;
        }
    }

    /// <summary>Refreshes the denormalised SKU when the listing behind it changes.</summary>
    /// <param name="sku">The variant's SKU.</param>
    public void RenameSku(string sku)
    {
        if (!string.IsNullOrWhiteSpace(sku))
        {
            Sku = sku.Trim();
        }
    }

    /// <summary>
    /// Records that the low-stock alert has been raised, or clears it once the item is back above
    /// its level. Returns whether an alert should be published now.
    /// </summary>
    /// <remarks>
    /// The decision and the bookkeeping are one call on purpose: two callers who each did half of
    /// this would either alert twice or never clear the flag.
    /// </remarks>
    /// <param name="at">When.</param>
    public bool TryRaiseLowStock(DateTimeOffset at)
    {
        if (!IsLow)
        {
            LowStockNotifiedAt = null;
            return false;
        }

        if (LowStockNotifiedAt is not null)
        {
            return false;
        }

        LowStockNotifiedAt = at;
        return true;
    }

    /// <summary>
    /// Applies a movement to the in-memory caches, for the one caller that is allowed to: the
    /// ledger service, after the database has already applied the same movement atomically.
    /// </summary>
    /// <remarks>
    /// This does not decide anything and it does not protect anything. It exists so the entity a
    /// handler is holding matches the row the conditional <c>UPDATE</c> just wrote, and so the
    /// ledger entry can carry the balances that update returned.
    /// </remarks>
    /// <param name="quantityOnHand">On hand, as the database now holds it.</param>
    /// <param name="quantityReserved">Reserved, as the database now holds it.</param>
    internal void SyncQuantities(int quantityOnHand, int quantityReserved)
    {
        QuantityOnHand = quantityOnHand;
        QuantityReserved = quantityReserved;
    }
}
