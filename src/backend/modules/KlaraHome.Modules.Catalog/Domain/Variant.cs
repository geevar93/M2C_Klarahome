using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Catalog.Domain;

/// <summary>Where a variant is in its life.</summary>
/// <remarks>
/// Deliberately smaller than the product's machine. A variant is never moderated on its own — the
/// product it belongs to is — so it has no pending state; it is either sellable or it is not.
/// </remarks>
internal enum VariantStatus
{
    /// <summary>Being described. Cannot be listed.</summary>
    Draft = 0,

    /// <summary>Sellable, provided its product is published too.</summary>
    Active = 1,

    /// <summary>Withdrawn. Existing listings against it stop being purchasable.</summary>
    Inactive = 2,

    /// <summary>Retired for good. Terminal, because order lines point at it.</summary>
    Archived = 3,
}

/// <summary>
/// A concretely specified sellable thing — colour Beige, size 40×40 (docs/02-domain-model.md §3).
/// </summary>
/// <remarks>
/// <para>
/// This is the row a SKU names, the row stock attaches to, and the row a vendor makes an offer
/// against. It carries the physical facts (weight and dimensions, which decide a shipping rate) and
/// the two India compliance fields that vary by pack size rather than by product: MRP and net
/// quantity.
/// </para>
/// <para>
/// <see cref="AttributeHash"/> is what enforces "a variant's defining attribute combination is
/// unique within its product". The combination lives in a child table and no database can put a
/// unique index across rows, so the combination is folded into one deterministic hash on this row
/// and the index goes there. Recomputing it is the only correct way to change the combination.
/// </para>
/// </remarks>
internal sealed class Variant : AggregateRoot<Guid>, ITenantScoped, IAuditable, ISoftDeletable
{
    private Variant(Guid id, Guid productId, string sku)
        : base(id)
    {
        ProductId = Guard.NotEmpty(productId);
        Sku = Guard.NotNullOrWhiteSpace(sku);
        Status = VariantStatus.Draft;
        Mrp = Money.Rupees(0m);
        AttributeHash = EmptyCombinationHash;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Variant()
    {
        Sku = string.Empty;
        Mrp = Money.Rupees(0m);
        AttributeHash = string.Empty;
    }

    /// <summary>The product this is a variant of.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>The stock-keeping unit. Unique per tenant; printed on labels and quoted in support.</summary>
    public string Sku { get; private set; }

    /// <summary>The EAN, UPC or ISBN on the pack, where there is one.</summary>
    public string? Barcode { get; private set; }

    /// <summary>What distinguishes it, appended to the product name — "Beige, 40×40 cm".</summary>
    public string? NameSuffix { get; private set; }

    /// <summary>Dead weight in grams. Read by every shipping rate card.</summary>
    public int WeightGrams { get; private set; }

    /// <summary>Packed length in millimetres, for volumetric weight.</summary>
    public int LengthMm { get; private set; }

    /// <summary>Packed width in millimetres.</summary>
    public int WidthMm { get; private set; }

    /// <summary>Packed height in millimetres.</summary>
    public int HeightMm { get; private set; }

    /// <summary>
    /// Maximum retail price. Statutory in India, always displayed, and the ceiling every offer
    /// price is checked against.
    /// </summary>
    public Money Mrp { get; private set; }

    /// <summary>The declared net quantity — "2 pieces", "500 g" (Legal Metrology).</summary>
    public string? NetQuantity { get; private set; }

    /// <summary>Shelf life in days from packing, for a perishable. Null where it does not apply.</summary>
    public int? ShelfLifeDays { get; private set; }

    /// <summary>A fixed expiry date, where the goods carry one rather than a shelf life.</summary>
    public DateOnly? ExpiresOn { get; private set; }

    /// <summary>Where it is in its life.</summary>
    public VariantStatus Status { get; private set; }

    /// <summary>Sort order within the product.</summary>
    public int Position { get; private set; }

    /// <summary>Whether the PDP opens on this variant. Exactly one per product should be true.</summary>
    public bool IsDefault { get; private set; }

    /// <summary>
    /// A deterministic hash of the defining attribute combination, unique within the product.
    /// </summary>
    public string AttributeHash { get; private set; }

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

    /// <summary>Whether the variant itself is sellable. Its product must be published too.</summary>
    public bool IsSellable => Status == VariantStatus.Active;

    /// <summary>The hash of a product with a single, undifferentiated variant.</summary>
    /// <remarks>
    /// A real value rather than an empty string, so the unique index still catches the mistake of
    /// creating two "no options" variants on one product.
    /// </remarks>
    public static readonly string EmptyCombinationHash = HashOf([]);

    /// <summary>Creates a variant of a product.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="sku">The SKU, already normalised and known to be free.</param>
    public static Variant Create(Guid productId, string sku) => new(UuidV7.New(), productId, sku);

    /// <summary>
    /// Whether the life cycle allows one status to follow another.
    /// </summary>
    /// <param name="from">The current status.</param>
    /// <param name="to">The status being moved to.</param>
    public static bool IsTransitionAllowed(VariantStatus from, VariantStatus to)
        => (from, to) switch
        {
            (VariantStatus.Draft, VariantStatus.Active) => true,
            (VariantStatus.Active, VariantStatus.Inactive) => true,
            (VariantStatus.Inactive, VariantStatus.Active) => true,
            (_, VariantStatus.Archived) => from != VariantStatus.Archived,
            _ => false,
        };

    /// <summary>Moves the variant, or returns false if the life cycle does not allow it.</summary>
    /// <param name="next">The status to move to.</param>
    public bool TransitionTo(VariantStatus next)
    {
        if (!IsTransitionAllowed(Status, next))
        {
            return false;
        }

        Status = next;
        return true;
    }

    /// <summary>
    /// The variant half of the mandatory-disclosure rule (docs/02-domain-model.md §7.4).
    /// </summary>
    /// <remarks>
    /// MRP and net quantity are per pack, not per product: a 500 g jar and a 1 kg jar of the same
    /// thing carry different declarations, so the check has to be here rather than on
    /// <see cref="Product"/>.
    /// </remarks>
    public IReadOnlyList<string> ComplianceGaps()
    {
        var gaps = new List<string>();

        if (Mrp.Amount <= 0m)
        {
            gaps.Add($"SKU {Sku} needs an MRP before it can be sold.");
        }

        if (string.IsNullOrWhiteSpace(NetQuantity))
        {
            gaps.Add($"SKU {Sku} needs a declared net quantity (Legal Metrology).");
        }

        if (WeightGrams <= 0)
        {
            gaps.Add($"SKU {Sku} needs a weight before a courier can be priced for it.");
        }

        return gaps;
    }

    /// <summary>Updates what the variant is and what it weighs.</summary>
    /// <param name="sku">The stock-keeping unit.</param>
    /// <param name="barcode">The barcode on the pack.</param>
    /// <param name="nameSuffix">What distinguishes it.</param>
    /// <param name="position">Sort order within the product.</param>
    public void Describe(string sku, string? barcode, string? nameSuffix, int position)
    {
        Sku = Guard.NotNullOrWhiteSpace(sku);
        Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        NameSuffix = string.IsNullOrWhiteSpace(nameSuffix) ? null : nameSuffix.Trim();
        Position = Math.Max(position, 0);
    }

    /// <summary>Records the packed dimensions and weight a courier is priced from.</summary>
    /// <param name="weightGrams">Dead weight.</param>
    /// <param name="lengthMm">Packed length.</param>
    /// <param name="widthMm">Packed width.</param>
    /// <param name="heightMm">Packed height.</param>
    public void SetDimensions(int weightGrams, int lengthMm, int widthMm, int heightMm)
    {
        WeightGrams = Math.Max(weightGrams, 0);
        LengthMm = Math.Max(lengthMm, 0);
        WidthMm = Math.Max(widthMm, 0);
        HeightMm = Math.Max(heightMm, 0);
    }

    /// <summary>Records the pack-level compliance declarations.</summary>
    /// <param name="mrp">Maximum retail price.</param>
    /// <param name="netQuantity">The declared net quantity.</param>
    /// <param name="shelfLifeDays">Shelf life in days, for a perishable.</param>
    /// <param name="expiresOn">A fixed expiry date, where the goods carry one.</param>
    public void DeclarePack(Money mrp, string? netQuantity, int? shelfLifeDays, DateOnly? expiresOn)
    {
        Mrp = mrp;
        NetQuantity = string.IsNullOrWhiteSpace(netQuantity) ? null : netQuantity.Trim();
        ShelfLifeDays = shelfLifeDays is > 0 ? shelfLifeDays : null;
        ExpiresOn = expiresOn;
    }

    /// <summary>Makes this the variant the PDP opens on, or stops it being.</summary>
    /// <param name="isDefault">Whether it is the default.</param>
    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    /// <summary>Records the hash of a newly written defining combination.</summary>
    /// <param name="pairs">The combination, as attribute id and option id.</param>
    public void SetCombination(IReadOnlyCollection<(Guid AttributeId, Guid OptionId)> pairs)
        => AttributeHash = HashOf(pairs);

    /// <summary>Retires the variant. Order lines point at it, so it is never actually removed.</summary>
    /// <param name="at">When.</param>
    public void Delete(DateTimeOffset at)
    {
        DeletedAt = at;
        Status = VariantStatus.Archived;
    }

    /// <summary>
    /// Folds a defining combination into one stable hash.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sorted before hashing, so <c>{colour: beige, size: 40}</c> and <c>{size: 40, colour: beige}</c>
    /// are the same combination — which they are, and a caller must not have to remember to order
    /// them.
    /// </para>
    /// <para>
    /// SHA-256 rather than <c>string.GetHashCode</c>: this value is written to a database and
    /// compared across processes and releases, and the runtime's string hash is randomised per
    /// process. Using it here would make the uniqueness index silently stop working after a
    /// restart.
    /// </para>
    /// </remarks>
    /// <param name="pairs">The combination, as attribute id and option id.</param>
    public static string HashOf(IReadOnlyCollection<(Guid AttributeId, Guid OptionId)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        var builder = new StringBuilder();

        foreach (var pair in pairs.OrderBy(pair => pair.AttributeId).ThenBy(pair => pair.OptionId))
        {
            builder.Append(CultureInfo.InvariantCulture, $"{pair.AttributeId:N}:{pair.OptionId:N};");
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}

/// <summary>
/// One axis of a variant's defining combination (docs/03-database-design.md §4.4).
/// </summary>
/// <remarks>
/// Always an option reference, never free text: only a closed list can be an axis, and
/// <see cref="ProductAttribute.CanDefineVariants"/> is the rule that says so.
/// </remarks>
internal sealed class VariantAttributeValue : Entity<Guid>, ITenantScoped
{
    private VariantAttributeValue(Guid id, Guid variantId, Guid attributeId, Guid optionId)
        : base(id)
    {
        VariantId = Guard.NotEmpty(variantId);
        AttributeId = Guard.NotEmpty(attributeId);
        OptionId = Guard.NotEmpty(optionId);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private VariantAttributeValue()
    {
    }

    /// <summary>The variant.</summary>
    public Guid VariantId { get; private set; }

    /// <summary>The axis.</summary>
    public Guid AttributeId { get; private set; }

    /// <summary>The value on that axis.</summary>
    public Guid OptionId { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Pins one axis of a variant.</summary>
    /// <param name="variantId">The variant.</param>
    /// <param name="attributeId">The axis.</param>
    /// <param name="optionId">The value on it.</param>
    public static VariantAttributeValue Create(Guid variantId, Guid attributeId, Guid optionId)
        => new(UuidV7.New(), variantId, attributeId, optionId);
}
