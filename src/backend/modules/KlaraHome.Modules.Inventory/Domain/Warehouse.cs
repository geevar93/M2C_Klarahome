using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Inventory.Domain;

/// <summary>
/// The postal address of a stock location, stored as JSON (docs/03-database-design.md §4.5).
/// </summary>
/// <remarks>
/// JSON rather than columns, unlike a vendor's pickup location, because nothing queries the inside
/// of it. Serviceability and rate cards key on the PIN code, and that is a column on the warehouse
/// itself for exactly that reason.
/// </remarks>
internal sealed class WarehouseAddress
{
    /// <summary>Building, unit and street.</summary>
    public string Line1 { get; set; } = string.Empty;

    /// <summary>Area or locality.</summary>
    public string? Line2 { get; set; }

    /// <summary>A nearby landmark. Couriers in India navigate by these.</summary>
    public string? Landmark { get; set; }

    /// <summary>City or town.</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>The <c>platform.states</c> row for the state or union territory.</summary>
    public Guid? StateId { get; set; }

    /// <summary>Who the courier asks for on arrival.</summary>
    public string? ContactName { get; set; }

    /// <summary>The number the courier rings, in E.164.</summary>
    public string? ContactPhone { get; set; }
}

/// <summary>
/// A place stock is held (docs/03-database-design.md §4.5).
/// </summary>
/// <remarks>
/// <para>
/// Platform-owned when <see cref="VendorId"/> is null, and a seller's own otherwise. Both exist in
/// the same table because a stock item points at exactly one warehouse and must not care which
/// kind it is — a fulfilled-by-platform listing and a seller-shipped one are the same row shape.
/// </para>
/// <para>
/// <see cref="Priority"/> orders the locations an allocator walks when it decides where units come
/// from. Lower wins. It is not a uniqueness constraint: two warehouses may share a priority, in
/// which case the tie is broken on the code, so the order is at least reproducible.
/// </para>
/// </remarks>
internal sealed class Warehouse : AggregateRoot<Guid>, ITenantScoped, IAuditable, IVendorScoped
{
    private Warehouse(Guid id, Guid? vendorId, string code, string name, string pincode)
        : base(id)
    {
        VendorId = vendorId;
        Code = Guard.NotNullOrWhiteSpace(code);
        Name = Guard.NotNullOrWhiteSpace(name);
        Pincode = Guard.NotNullOrWhiteSpace(pincode);
        Address = new WarehouseAddress();
        IsActive = true;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Warehouse()
    {
        Code = string.Empty;
        Name = string.Empty;
        Pincode = string.Empty;
        Address = new WarehouseAddress();
    }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>The short code an operator quotes: <c>BLR-01</c>. Unique within the tenant.</summary>
    public string Code { get; private set; }

    /// <summary>What it is called on a screen.</summary>
    public string Name { get; private set; }

    /// <summary>Where it is. Not queried, so it is JSON.</summary>
    public WarehouseAddress Address { get; private set; }

    /// <summary>Six-digit PIN code. Shipping prices the first leg from this, so it is a column.</summary>
    public string Pincode { get; private set; }

    /// <summary>Whether stock may still move through here.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Allocation order. Lower wins.</summary>
    public int Priority { get; private set; } = DefaultPriority;

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

    /// <summary>The middle of the range, so a new warehouse can be ordered either side of it.</summary>
    public const int DefaultPriority = 100;

    /// <summary>The highest priority number this platform stores.</summary>
    public const int MaxPriority = 1000;

    /// <summary>Opens a location.</summary>
    /// <param name="vendorId">The seller who owns it, or null for a platform warehouse.</param>
    /// <param name="code">The short code an operator quotes.</param>
    /// <param name="name">What it is called.</param>
    /// <param name="pincode">Six-digit PIN code.</param>
    public static Warehouse Open(Guid? vendorId, string code, string name, string pincode)
        => new(UuidV7.New(), vendorId, code.Trim().ToUpperInvariant(), name, pincode);

    /// <summary>Renames the location and restates where it is.</summary>
    /// <param name="name">What it is called.</param>
    /// <param name="pincode">Six-digit PIN code.</param>
    /// <param name="address">Where it is.</param>
    /// <param name="priority">Allocation order.</param>
    public void Update(string name, string pincode, WarehouseAddress address, int priority)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Pincode = Guard.NotNullOrWhiteSpace(pincode);
        Address = Guard.NotNull(address);
        Priority = Math.Clamp(priority, 0, MaxPriority);
    }

    /// <summary>Opens or closes the location to stock movement.</summary>
    /// <param name="isActive">Whether stock may still move through here.</param>
    public void SetActive(bool isActive) => IsActive = isActive;
}
