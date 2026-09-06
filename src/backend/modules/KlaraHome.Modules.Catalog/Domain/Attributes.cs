using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Catalog.Domain;

/// <summary>
/// What kind of value an attribute holds (docs/03-database-design.md §4.4).
/// </summary>
/// <remarks>
/// The type decides which of the four value columns a
/// <see cref="ProductAttributeValue"/> writes into. Typed columns rather than one text column
/// because a facet on "under 500 grams" is a numeric comparison, and a text column would make it a
/// full scan and a cast that fails on the one row somebody typed "approx 400g" into.
/// </remarks>
internal enum AttributeDataType
{
    /// <summary>Free text. Never a facet, occasionally searchable.</summary>
    Text = 0,

    /// <summary>A number, with an optional unit. Range facets read this.</summary>
    Number = 1,

    /// <summary>Yes or no. Renders as a checkbox facet.</summary>
    Boolean = 2,

    /// <summary>One value from a closed list of <see cref="AttributeOption"/>s.</summary>
    Select = 3,

    /// <summary>Several values from that list.</summary>
    MultiSelect = 4,

    /// <summary>A calendar date. Used for things like a certification's expiry.</summary>
    Date = 5,
}

/// <summary>
/// A named property a product or a variant can carry — colour, size, material, wattage
/// (docs/03-database-design.md §4.4).
/// </summary>
/// <remarks>
/// <para>
/// Named <c>ProductAttribute</c> rather than <c>Attribute</c> deliberately: <c>System.Attribute</c>
/// is in every file's implicit usings, and a domain type that shadows it produces error messages
/// nobody can read.
/// </para>
/// <para>
/// <see cref="IsVariantDefining"/> is the flag that matters most. A defining attribute is an
/// <em>axis</em> — colour and size make Beige/40×40 a different SKU from Grey/60×60 — and a
/// non-defining one merely describes the product. Getting it wrong produces either a product whose
/// variants are indistinguishable or a variant explosion nobody meant to create.
/// </para>
/// </remarks>
internal sealed class ProductAttribute : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private ProductAttribute(Guid id, string code, string name, AttributeDataType dataType)
        : base(id)
    {
        Code = Guard.NotNullOrWhiteSpace(code);
        Name = Guard.NotNullOrWhiteSpace(name);
        DataType = dataType;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ProductAttribute()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>The stable lowercase code. This is the key a facet query uses: <c>attr.color</c>.</summary>
    public string Code { get; private set; }

    /// <summary>The label a shopper sees.</summary>
    public string Name { get; private set; }

    /// <summary>What kind of value it holds.</summary>
    public AttributeDataType DataType { get; private set; }

    /// <summary>Whether this attribute is one of the axes that distinguish a variant.</summary>
    public bool IsVariantDefining { get; private set; }

    /// <summary>Whether the storefront offers it as a filter.</summary>
    public bool IsFilterable { get; private set; }

    /// <summary>Whether its values are folded into the product's search text.</summary>
    public bool IsSearchable { get; private set; }

    /// <summary>Whether a product must supply a value before it can be published.</summary>
    public bool IsRequired { get; private set; }

    /// <summary>The unit its numbers are in — <c>g</c>, <c>cm</c>, <c>W</c>. Null for everything else.</summary>
    public string? Unit { get; private set; }

    /// <summary>Sort order in the specification table and the filter rail.</summary>
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

    /// <summary>Whether values for this attribute come from a closed list.</summary>
    public bool UsesOptions => DataType is AttributeDataType.Select or AttributeDataType.MultiSelect;

    /// <summary>
    /// Whether this attribute may be used as a variant axis at all.
    /// </summary>
    /// <remarks>
    /// Only a closed list can be one. An axis has to be comparable and enumerable — the storefront
    /// renders it as a swatch row and the uniqueness rule hashes it — and free text is neither.
    /// </remarks>
    public bool CanDefineVariants => UsesOptions;

    /// <summary>Declares an attribute.</summary>
    /// <param name="code">The stable code, already normalised and known to be free.</param>
    /// <param name="name">The shopper-facing label.</param>
    /// <param name="dataType">What kind of value it holds.</param>
    public static ProductAttribute Create(string code, string name, AttributeDataType dataType)
        => new(UuidV7.New(), code, name, dataType);

    /// <summary>
    /// Updates the attribute. The data type is deliberately not changeable.
    /// </summary>
    /// <remarks>
    /// Changing <c>select</c> to <c>number</c> would strand every value already written into the
    /// option column, and there is no correct way to reinterpret them. A different type is a
    /// different attribute.
    /// </remarks>
    /// <param name="name">The shopper-facing label.</param>
    /// <param name="unit">The unit its numbers are in.</param>
    /// <param name="isVariantDefining">Whether it is a variant axis.</param>
    /// <param name="isFilterable">Whether it is offered as a filter.</param>
    /// <param name="isSearchable">Whether it feeds the search text.</param>
    /// <param name="isRequired">Whether a product must supply it before publishing.</param>
    /// <param name="position">Sort order.</param>
    public void Describe(
        string name,
        string? unit,
        bool isVariantDefining,
        bool isFilterable,
        bool isSearchable,
        bool isRequired,
        int position)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();

        // Silently refusing would be worse than the caller's request being partly ignored: a text
        // attribute marked as an axis produces variants whose uniqueness cannot be computed, and
        // the application layer rejects the request before it gets here. This is the belt.
        IsVariantDefining = isVariantDefining && CanDefineVariants;
        IsFilterable = isFilterable;
        IsSearchable = isSearchable;
        IsRequired = isRequired;
        Position = Math.Max(position, 0);
    }
}

/// <summary>
/// One permitted value of a <c>select</c> or <c>multiselect</c> attribute
/// (docs/03-database-design.md §4.4).
/// </summary>
internal sealed class AttributeOption : Entity<Guid>, ITenantScoped, IAuditable
{
    private AttributeOption(Guid id, Guid attributeId, string value, string label)
        : base(id)
    {
        AttributeId = Guard.NotEmpty(attributeId);
        Value = Guard.NotNullOrWhiteSpace(value);
        Label = Guard.NotNullOrWhiteSpace(label);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private AttributeOption()
    {
        Value = string.Empty;
        Label = string.Empty;
    }

    /// <summary>The attribute this is an option of.</summary>
    public Guid AttributeId { get; private set; }

    /// <summary>The machine value, which is what a facet query filters on: <c>beige</c>.</summary>
    public string Value { get; private set; }

    /// <summary>The label a shopper sees: <c>Beige</c>.</summary>
    public string Label { get; private set; }

    /// <summary>A hex colour, for an attribute the storefront renders as a swatch.</summary>
    public string? SwatchHex { get; private set; }

    /// <summary>Sort order within the attribute.</summary>
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

    /// <summary>Adds a permitted value.</summary>
    /// <param name="attributeId">The attribute.</param>
    /// <param name="value">The machine value.</param>
    /// <param name="label">The shopper-facing label.</param>
    /// <param name="swatchHex">A hex colour, for a swatch.</param>
    /// <param name="position">Sort order.</param>
    public static AttributeOption Create(
        Guid attributeId,
        string value,
        string label,
        string? swatchHex,
        int position)
        => new(UuidV7.New(), attributeId, value, label)
        {
            SwatchHex = swatchHex,
            Position = Math.Max(position, 0),
        };

    /// <summary>Renames the option or moves it.</summary>
    /// <param name="label">The shopper-facing label.</param>
    /// <param name="swatchHex">A hex colour, for a swatch.</param>
    /// <param name="position">Sort order.</param>
    public void Describe(string label, string? swatchHex, int position)
    {
        Label = Guard.NotNullOrWhiteSpace(label);
        SwatchHex = swatchHex;
        Position = Math.Max(position, 0);
    }
}

/// <summary>
/// A named bundle of attributes that a category's products are described with
/// (docs/03-database-design.md §4.4).
/// </summary>
/// <remarks>
/// Without it, every merchandiser adding a lamp would have to remember which of two hundred
/// attributes apply to lamps. With it, the category names one set and the product form writes
/// itself.
/// </remarks>
internal sealed class AttributeSet : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private AttributeSet(Guid id, string code, string name)
        : base(id)
    {
        Code = Guard.NotNullOrWhiteSpace(code);
        Name = Guard.NotNullOrWhiteSpace(name);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private AttributeSet()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>The stable lowercase code.</summary>
    public string Code { get; private set; }

    /// <summary>What the set is called.</summary>
    public string Name { get; private set; }

    /// <summary>What it is for, for whoever picks one from a list of forty.</summary>
    public string? Description { get; private set; }

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

    /// <summary>Declares a set.</summary>
    /// <param name="code">The stable code, already normalised and known to be free.</param>
    /// <param name="name">What it is called.</param>
    public static AttributeSet Create(string code, string name) => new(UuidV7.New(), code, name);

    /// <summary>Renames the set.</summary>
    /// <param name="name">What it is called.</param>
    /// <param name="description">What it is for.</param>
    public void Describe(string name, string? description)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Description = description;
    }
}

/// <summary>Membership of an attribute in a set, with its position in that set's form.</summary>
internal sealed class AttributeSetMember : Entity<Guid>, ITenantScoped
{
    private AttributeSetMember(Guid id, Guid attributeSetId, Guid attributeId)
        : base(id)
    {
        AttributeSetId = Guard.NotEmpty(attributeSetId);
        AttributeId = Guard.NotEmpty(attributeId);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private AttributeSetMember()
    {
    }

    /// <summary>The set.</summary>
    public Guid AttributeSetId { get; private set; }

    /// <summary>The attribute in it.</summary>
    public Guid AttributeId { get; private set; }

    /// <summary>
    /// Whether this set makes the attribute mandatory, even though the attribute itself is not.
    /// </summary>
    /// <remarks>
    /// Wattage is optional in general and mandatory for a lamp. Requiredness is therefore a
    /// property of the membership, not only of the attribute.
    /// </remarks>
    public bool IsRequired { get; private set; }

    /// <summary>Sort order within the set.</summary>
    public int Position { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Puts an attribute in a set.</summary>
    /// <param name="attributeSetId">The set.</param>
    /// <param name="attributeId">The attribute.</param>
    /// <param name="isRequired">Whether this set makes it mandatory.</param>
    /// <param name="position">Sort order.</param>
    public static AttributeSetMember Create(Guid attributeSetId, Guid attributeId, bool isRequired, int position)
        => new(UuidV7.New(), attributeSetId, attributeId)
        {
            IsRequired = isRequired,
            Position = Math.Max(position, 0),
        };
}
