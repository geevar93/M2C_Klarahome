using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Identity.Domain;

/// <summary>
/// A named bundle of permissions (docs/07-security-compliance.md §2). Roles are data, not code, so
/// a redistributed deployment can define its own without a build.
/// </summary>
/// <remarks>
/// Enforcement is never on a role. An endpoint asks for a permission; a role is only how a human
/// hands several of them out at once. That is why renaming or reshaping a role can never open an
/// endpoint that was closed.
/// </remarks>
internal sealed class Role : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private readonly List<RolePermission> _permissions = [];

    private Role(Guid id, string code, string name, RoleScope scope, bool isSystem, string description)
        : base(id)
    {
        Code = Guard.NotNullOrWhiteSpace(code);
        Name = Guard.NotNullOrWhiteSpace(name);
        Scope = scope;
        IsSystem = isSystem;
        Description = description ?? string.Empty;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Role()
    {
        Code = string.Empty;
        Name = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Stable lowercase code, unique within the tenant. Referenced by seeders and tests.</summary>
    public string Code { get; private set; }

    /// <summary>Display name shown in the admin UI.</summary>
    public string Name { get; private set; }

    /// <summary>Whether this role is granted platform-wide or within one vendor.</summary>
    public RoleScope Scope { get; private set; }

    /// <summary>
    /// Whether the platform itself defines this role. A system role may be granted and its
    /// permissions viewed, but it may not be renamed, reshaped or deleted — its permission set is
    /// what the seeder asserts on every deploy, so an edit would be silently undone.
    /// </summary>
    public bool IsSystem { get; private set; }

    /// <summary>What the role is for, so the admin UI is not a list of bare codes.</summary>
    public string Description { get; private set; }

    /// <summary>The permissions this role grants.</summary>
    public IReadOnlyList<RolePermission> Permissions => _permissions;

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

    /// <summary>Defines a role.</summary>
    /// <param name="code">The stable lowercase code.</param>
    /// <param name="name">The display name.</param>
    /// <param name="scope">Platform-wide or vendor-scoped.</param>
    /// <param name="description">What the role is for.</param>
    /// <param name="isSystem">Whether the platform defines it.</param>
    public static Role Define(
        string code,
        string name,
        RoleScope scope,
        string description = "",
        bool isSystem = false)
        => new(UuidV7.New(), code, name, scope, isSystem, description);

    /// <summary>Renames the role and updates its description. The code is immutable.</summary>
    /// <param name="name">The new display name.</param>
    /// <param name="description">The new description.</param>
    public void Rename(string name, string description)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Description = description ?? string.Empty;
    }

    /// <summary>Replaces the role's permission set as a whole, which is how the admin UI edits it.</summary>
    /// <param name="permissionCodes">The permissions the role should grant.</param>
    public void SetPermissions(IEnumerable<string> permissionCodes)
    {
        ArgumentNullException.ThrowIfNull(permissionCodes);

        var wanted = permissionCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        _permissions.RemoveAll(held => !wanted.Contains(held.PermissionCode, StringComparer.Ordinal));

        foreach (var code in wanted)
        {
            if (!_permissions.Exists(held => string.Equals(held.PermissionCode, code, StringComparison.Ordinal)))
            {
                _permissions.Add(RolePermission.Create(Id, code));
            }
        }
    }
}

/// <summary>Whether a role is granted platform-wide or inside one vendor.</summary>
internal enum RoleScope
{
    /// <summary>Granted to platform staff. Sees every vendor.</summary>
    Platform = 0,

    /// <summary>Granted inside one vendor. Every grant carries a vendor id.</summary>
    Vendor = 1,

    /// <summary>Granted to shoppers. Carries only the permissions a customer needs.</summary>
    Customer = 2,
}

/// <summary>One permission granted by one role.</summary>
/// <remarks>
/// The permission is stored by its code rather than by a foreign key to <see cref="Permission"/>.
/// The catalogue is declared in code and reconciled into the table on every deploy; a code that
/// has been retired should leave a role entry that is visibly stale, not block the migration that
/// retired it.
/// </remarks>
internal sealed class RolePermission : Entity<Guid>, ITenantScoped
{
    private RolePermission(Guid id, Guid roleId, string permissionCode)
        : base(id)
    {
        RoleId = roleId;
        PermissionCode = Guard.NotNullOrWhiteSpace(permissionCode);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private RolePermission() => PermissionCode = string.Empty;

    /// <summary>The role granting the permission.</summary>
    public Guid RoleId { get; private set; }

    /// <summary>The granular permission code.</summary>
    public string PermissionCode { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Creates a grant.</summary>
    /// <param name="roleId">The role.</param>
    /// <param name="permissionCode">The permission code.</param>
    public static RolePermission Create(Guid roleId, string permissionCode)
        => new(UuidV7.New(), roleId, permissionCode);
}

/// <summary>
/// One granular permission, as the admin UI needs to display it: grouped and described.
/// </summary>
/// <remarks>
/// The rows are a projection of the catalogue declared in code, reconciled by a seeder on every
/// deploy. Nothing reads this table to decide an authorisation — a permission exists because an
/// endpoint declares it, not because a row says so.
/// </remarks>
internal sealed class Permission : Entity<Guid>, ITenantScoped
{
    private Permission(Guid id, string code, string group, string description)
        : base(id)
    {
        Code = Guard.NotNullOrWhiteSpace(code);
        Group = Guard.NotNullOrWhiteSpace(group);
        Description = description ?? string.Empty;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Permission()
    {
        Code = string.Empty;
        Group = string.Empty;
        Description = string.Empty;
    }

    /// <summary>The dotted lowercase permission code.</summary>
    public string Code { get; private set; }

    /// <summary>The heading the admin UI files it under, for example <c>Orders</c>.</summary>
    public string Group { get; private set; }

    /// <summary>What holding it lets a user do.</summary>
    public string Description { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Creates a catalogue row.</summary>
    /// <param name="code">The permission code.</param>
    /// <param name="group">The admin UI grouping.</param>
    /// <param name="description">What it allows.</param>
    public static Permission Create(string code, string group, string description)
        => new(UuidV7.New(), code, group, description);

    /// <summary>Updates the display metadata when the catalogue's wording changes.</summary>
    /// <param name="group">The admin UI grouping.</param>
    /// <param name="description">What it allows.</param>
    public void Describe(string group, string description)
    {
        Group = Guard.NotNullOrWhiteSpace(group);
        Description = description ?? string.Empty;
    }
}

/// <summary>
/// One role granted to one user, optionally inside one vendor.
/// </summary>
/// <remarks>
/// This is the row that makes the vendor scope real. It is <see cref="IVendorScoped"/>, so a
/// vendor caller's queries see only grants inside their own vendor — including when they list the
/// users of "their" organisation, which is how a vendor owner is prevented from discovering that
/// another vendor's staff exist at all.
/// </remarks>
internal sealed class UserRole : Entity<Guid>, ITenantScoped, IVendorScoped
{
    private UserRole(Guid id, Guid userId, Guid roleId, Guid? vendorId)
        : base(id)
    {
        UserId = userId;
        RoleId = roleId;
        VendorId = vendorId;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private UserRole()
    {
    }

    /// <summary>The user holding the role.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The role held.</summary>
    public Guid RoleId { get; private set; }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Creates a grant.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="roleId">The role.</param>
    /// <param name="vendorId">The vendor scope, or null for a platform-wide grant.</param>
    public static UserRole Create(Guid userId, Guid roleId, Guid? vendorId)
        => new(UuidV7.New(), userId, roleId, vendorId);
}
