using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Vendors.Domain;

/// <summary>How widely a serviceability rule reaches.</summary>
internal enum ServiceableRegionScope
{
    /// <summary>A whole state or union territory.</summary>
    State = 0,

    /// <summary>
    /// A PIN code prefix. Two digits is a postal circle, three a sorting region, six one delivery
    /// office — so one column expresses "all of Maharashtra's 40xxxx" and "only 400001" alike.
    /// </summary>
    PincodePrefix = 1,
}

/// <summary>
/// Somewhere a seller is, or is not, willing to deliver.
/// </summary>
/// <remarks>
/// <para>
/// This is the seller's own commercial choice, not the courier's reach. Shipping asks the
/// aggregator whether a parcel <em>can</em> get to a PIN code (Step 16); this answers whether this
/// seller wants it to — a furniture seller who will not ship beyond their own state has a courier
/// who would happily take it.
/// </para>
/// <para>
/// A seller with <see cref="Vendor.ServesAllIndia"/> set has no rows at all. Once it is cleared,
/// the rows are an allow-list with exclusions: an included rule opens a region, and an excluded one
/// takes a hole out of it, so "all of Karnataka except Bangalore rural" is two rows rather than a
/// hundred.
/// </para>
/// </remarks>
internal sealed class VendorServiceableRegion : AggregateRoot<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private VendorServiceableRegion(Guid id, Guid vendorId, ServiceableRegionScope scope, bool isExcluded)
        : base(id)
    {
        VendorId = vendorId;
        Scope = scope;
        IsExcluded = isExcluded;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private VendorServiceableRegion()
    {
    }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>Whether the rule names a state or a PIN code prefix.</summary>
    public ServiceableRegionScope Scope { get; private set; }

    /// <summary>The <c>platform.states</c> row, for a state rule.</summary>
    public Guid? StateId { get; private set; }

    /// <summary>The PIN code prefix, for a PIN code rule. Two to six digits.</summary>
    public string? PincodePrefix { get; private set; }

    /// <summary>Whether this rule takes a region away rather than adding one.</summary>
    public bool IsExcluded { get; private set; }

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

    /// <summary>Adds a rule covering a whole state.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="stateId">The state.</param>
    /// <param name="isExcluded">Whether the state is being taken away rather than added.</param>
    public static VendorServiceableRegion ForState(Guid vendorId, Guid stateId, bool isExcluded = false)
        => new(UuidV7.New(), vendorId, ServiceableRegionScope.State, isExcluded)
        {
            StateId = Guard.NotEmpty(stateId),
        };

    /// <summary>Adds a rule covering a PIN code prefix.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="prefix">Two to six digits.</param>
    /// <param name="isExcluded">Whether the range is being taken away rather than added.</param>
    public static VendorServiceableRegion ForPincode(Guid vendorId, string prefix, bool isExcluded = false)
        => new(UuidV7.New(), vendorId, ServiceableRegionScope.PincodePrefix, isExcluded)
        {
            PincodePrefix = Guard.NotNullOrWhiteSpace(prefix).Trim(),
        };
}
