using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Vendors.Domain;

/// <summary>
/// A place a courier collects from (docs/03-database-design.md §4.3).
/// </summary>
/// <remarks>
/// <para>
/// Columns rather than JSON, unlike the seller's registered address, because this one is queried:
/// shipping picks the pickup point nearest the destination, prices the leg from its PIN code, and
/// asks the aggregator whether that PIN code is served. A JSON blob would make every one of those
/// a scan.
/// </para>
/// <para>
/// <see cref="CourierLocationCode"/> is the aggregator's own id for this address. Couriers require
/// a pickup location to be registered with them before a shipment can be booked against it, so the
/// code is the evidence that registration happened — null means Step 16 has not registered it yet.
/// </para>
/// </remarks>
internal sealed class VendorPickupLocation : AggregateRoot<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private VendorPickupLocation(
        Guid id,
        Guid vendorId,
        string label,
        string contactName,
        string contactPhone,
        string line1,
        string city,
        Guid stateId,
        string pincode)
        : base(id)
    {
        VendorId = vendorId;
        Label = Guard.NotNullOrWhiteSpace(label);
        ContactName = Guard.NotNullOrWhiteSpace(contactName);
        ContactPhone = Guard.NotNullOrWhiteSpace(contactPhone);
        Line1 = Guard.NotNullOrWhiteSpace(line1);
        City = Guard.NotNullOrWhiteSpace(city);
        StateId = Guard.NotEmpty(stateId);
        Pincode = Guard.NotNullOrWhiteSpace(pincode);
        IsActive = true;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private VendorPickupLocation()
    {
        Label = string.Empty;
        ContactName = string.Empty;
        ContactPhone = string.Empty;
        Line1 = string.Empty;
        City = string.Empty;
        Pincode = string.Empty;
    }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>What the seller calls it: Warehouse, Andheri store, Factory.</summary>
    public string Label { get; private set; }

    /// <summary>Who the courier asks for on arrival.</summary>
    public string ContactName { get; private set; }

    /// <summary>The number the courier rings, in E.164.</summary>
    public string ContactPhone { get; private set; }

    /// <summary>Building and unit.</summary>
    public string Line1 { get; private set; }

    /// <summary>Street, area or locality.</summary>
    public string? Line2 { get; private set; }

    /// <summary>A nearby landmark. Couriers in India navigate by these.</summary>
    public string? Landmark { get; private set; }

    /// <summary>City or town.</summary>
    public string City { get; private set; }

    /// <summary>The <c>platform.states</c> row for the state or union territory.</summary>
    public Guid StateId { get; private set; }

    /// <summary>Six-digit PIN code. Shipping prices the first leg from this.</summary>
    public string Pincode { get; private set; }

    /// <summary>Whether this is the location shipments are booked against by default.</summary>
    public bool IsDefault { get; private set; }

    /// <summary>Whether the seller still collects from here.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The logistics aggregator's own id for this address, once registered (Step 16).</summary>
    public string? CourierLocationCode { get; private set; }

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

    /// <summary>Records a place a courier may collect from.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="label">What the seller calls it.</param>
    /// <param name="contactName">Who the courier asks for.</param>
    /// <param name="contactPhone">The number they ring.</param>
    /// <param name="line1">Building and unit.</param>
    /// <param name="city">City or town.</param>
    /// <param name="stateId">The state.</param>
    /// <param name="pincode">Six-digit PIN code.</param>
    public static VendorPickupLocation Add(
        Guid vendorId,
        string label,
        string contactName,
        string contactPhone,
        string line1,
        string city,
        Guid stateId,
        string pincode)
        => new(UuidV7.New(), vendorId, label, contactName, contactPhone, line1, city, stateId, pincode);

    /// <summary>Updates the address and who to ask for.</summary>
    /// <param name="label">What the seller calls it.</param>
    /// <param name="contactName">Who the courier asks for.</param>
    /// <param name="contactPhone">The number they ring.</param>
    /// <param name="line1">Building and unit.</param>
    /// <param name="line2">Street, area or locality.</param>
    /// <param name="landmark">A nearby landmark.</param>
    /// <param name="city">City or town.</param>
    /// <param name="stateId">The state.</param>
    /// <param name="pincode">Six-digit PIN code.</param>
    public void Update(
        string label,
        string contactName,
        string contactPhone,
        string line1,
        string? line2,
        string? landmark,
        string city,
        Guid stateId,
        string pincode)
    {
        Label = Guard.NotNullOrWhiteSpace(label);
        ContactName = Guard.NotNullOrWhiteSpace(contactName);
        ContactPhone = Guard.NotNullOrWhiteSpace(contactPhone);
        Line1 = Guard.NotNullOrWhiteSpace(line1);
        Line2 = line2;
        Landmark = landmark;
        City = Guard.NotNullOrWhiteSpace(city);
        StateId = Guard.NotEmpty(stateId);
        Pincode = Guard.NotNullOrWhiteSpace(pincode);

        // The aggregator registered a specific address. Changing the address invalidates that
        // registration, and booking against a stale code is how a parcel gets collected from a
        // building the seller left.
        CourierLocationCode = null;
    }

    /// <summary>Makes this the default collection point, or takes that away.</summary>
    /// <param name="isDefault">Whether it is now the default.</param>
    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    /// <summary>Retires or reinstates the location.</summary>
    /// <param name="isActive">Whether the seller still collects from here.</param>
    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>Records the aggregator's id for this address (Step 16).</summary>
    /// <param name="courierLocationCode">The code the aggregator returned.</param>
    public void LinkCourierLocation(string courierLocationCode)
        => CourierLocationCode = Guard.NotNullOrWhiteSpace(courierLocationCode);
}
