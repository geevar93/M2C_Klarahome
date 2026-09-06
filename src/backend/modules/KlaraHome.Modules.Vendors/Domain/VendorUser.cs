using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Vendors.Domain;

/// <summary>
/// A person who operates a seller's account (docs/03-database-design.md §4.3).
/// </summary>
/// <remarks>
/// <para>
/// The row is the membership, not the person. The account itself — credentials, roles, sessions —
/// belongs to the Identity module, and this holds only a <c>user_id</c>, because no foreign key
/// may cross a schema boundary. What a vendor user is <em>allowed</em> to do is decided by the
/// roles Identity grants them; what they are allowed to do it <em>to</em> is decided by the
/// <c>vendor_id</c> in their token, which is this row's reason to exist.
/// </para>
/// <para>
/// Exactly one member is the owner. The owner is the legal signatory — they are who the platform
/// writes to about a suspension, and the last owner cannot be removed, because a seller with
/// nobody responsible for it is a seller nobody can be asked about a payout.
/// </para>
/// </remarks>
internal sealed class VendorUser : AggregateRoot<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private VendorUser(Guid id, Guid vendorId, Guid userId, bool isOwner)
        : base(id)
    {
        VendorId = vendorId;
        UserId = userId;
        IsOwner = isOwner;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private VendorUser()
    {
    }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>The <c>identity.users</c> row this membership is for.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Whether this member is the legal owner of the seller account.</summary>
    public bool IsOwner { get; private set; }

    /// <summary>What this member does for the seller. Free text; the permissions are their roles.</summary>
    public string? JobTitle { get; private set; }

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

    /// <summary>Adds a person to a seller's account.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="userId">The Identity account.</param>
    /// <param name="isOwner">Whether they are the legal owner.</param>
    /// <param name="jobTitle">What they do for the seller.</param>
    public static VendorUser Join(Guid vendorId, Guid userId, bool isOwner, string? jobTitle = null)
        => new(UuidV7.New(), vendorId, userId, isOwner) { JobTitle = jobTitle };

    /// <summary>Makes this member the owner, or takes the ownership away.</summary>
    /// <param name="isOwner">Whether they are now the owner.</param>
    public void SetOwner(bool isOwner) => IsOwner = isOwner;

    /// <summary>Records what they do for the seller.</summary>
    /// <param name="jobTitle">The job title.</param>
    public void Describe(string? jobTitle) => JobTitle = jobTitle;
}
