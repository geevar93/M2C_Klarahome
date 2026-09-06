using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Inventory.Domain;

/// <summary>
/// Somebody stock is bought from (docs/03-database-design.md §4.5).
/// </summary>
/// <remarks>
/// <para>
/// Not a vendor. A vendor sells <em>on</em> this marketplace and has an account, a KYC file and a
/// payout; a supplier is who a vendor or the platform buys from, and this platform never pays them
/// — that is between them and their supplier. The two are separate tables in separate schemas
/// precisely because conflating them would put a purchase order in the settlement run.
/// </para>
/// <para>
/// Vendor-scoped and nullable, like a warehouse: a seller keeps their own supplier list, the
/// platform keeps its own for platform-owned stock, and neither sees the other's.
/// </para>
/// </remarks>
internal sealed class Supplier : AggregateRoot<Guid>, ITenantScoped, IAuditable, IVendorScoped
{
    private Supplier(Guid id, Guid? vendorId, string code, string name)
        : base(id)
    {
        VendorId = vendorId;
        Code = Guard.NotNullOrWhiteSpace(code);
        Name = Guard.NotNullOrWhiteSpace(name);
        IsActive = true;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Supplier()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>The short code a buyer quotes on a purchase order. Unique within the tenant.</summary>
    public string Code { get; private set; }

    /// <summary>The supplier's trading name.</summary>
    public string Name { get; private set; }

    /// <summary>Who to speak to.</summary>
    public string? ContactName { get; private set; }

    /// <summary>Where the purchase order is emailed.</summary>
    public string? Email { get; private set; }

    /// <summary>The number to ring, in E.164.</summary>
    public string? Phone { get; private set; }

    /// <summary>Their GST registration, which decides the tax on the purchase.</summary>
    public string? Gstin { get; private set; }

    /// <summary>Where they are. Not queried, so it is JSON.</summary>
    public WarehouseAddress? Address { get; private set; }

    /// <summary>How many days after a receipt their invoice falls due.</summary>
    public int PaymentTermsDays { get; private set; }

    /// <summary>Whether new purchase orders may still be raised against them.</summary>
    public bool IsActive { get; private set; }

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

    /// <summary>The longest payment term this platform stores, in days.</summary>
    public const int MaxPaymentTermsDays = 365;

    /// <summary>Adds a supplier.</summary>
    /// <param name="vendorId">The seller whose list it belongs to, or null for the platform's.</param>
    /// <param name="code">The short code a buyer quotes.</param>
    /// <param name="name">Their trading name.</param>
    public static Supplier Add(Guid? vendorId, string code, string name)
        => new(UuidV7.New(), vendorId, code.Trim().ToUpperInvariant(), name);

    /// <summary>Restates who they are and how to reach them.</summary>
    /// <param name="name">Their trading name.</param>
    /// <param name="contactName">Who to speak to.</param>
    /// <param name="email">Where the purchase order is emailed.</param>
    /// <param name="phone">The number to ring.</param>
    /// <param name="gstin">Their GST registration.</param>
    /// <param name="address">Where they are.</param>
    /// <param name="paymentTermsDays">How many days their invoice falls due in.</param>
    public void Update(
        string name,
        string? contactName,
        string? email,
        string? phone,
        string? gstin,
        WarehouseAddress? address,
        int paymentTermsDays)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        ContactName = Trim(contactName);
        Email = Trim(email);
        Phone = Trim(phone);
        Gstin = Trim(gstin)?.ToUpperInvariant();
        Address = address;
        PaymentTermsDays = Math.Clamp(paymentTermsDays, 0, MaxPaymentTermsDays);
    }

    /// <summary>Opens or closes them to new purchase orders.</summary>
    /// <param name="isActive">Whether orders may still be raised.</param>
    public void SetActive(bool isActive) => IsActive = isActive;

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
