using KlaraHome.Modules.Identity.Domain;

namespace KlaraHome.Modules.Identity.Infrastructure.Seeding;

/// <summary>One permission, as the catalogue declares it.</summary>
/// <param name="Code">The dotted lowercase code an endpoint asks for.</param>
/// <param name="Group">The heading the admin UI files it under.</param>
/// <param name="Description">What holding it lets a user do.</param>
internal sealed record PermissionDescriptor(string Code, string Group, string Description);

/// <summary>
/// Every permission this platform enforces (docs/07-security-compliance.md §2).
/// </summary>
/// <remarks>
/// <para>
/// Declared in code, reconciled into <c>identity.permissions</c> on every deploy. The table is a
/// projection for the admin UI, never the authority: a permission exists because an endpoint
/// declares it with <c>RequirePermission</c>, and an integration test asserts that every permission
/// an endpoint asks for appears here. A row that no endpoint asks for is a role checkbox that does
/// nothing, which is worse than a missing one because it looks like a grant.
/// </para>
/// <para>
/// Permissions for modules that do not exist yet are deliberately absent. They are added by the
/// step that builds the endpoints asking for them, so this list and the enforced surface can never
/// drift apart.
/// </para>
/// </remarks>
internal static class PermissionCatalog
{
    /// <summary>Read the settings, branding and feature flags; change them.</summary>
    public const string PlatformSettingsManage = "platform.settings.manage";

    /// <summary>Read the audit trail.</summary>
    public const string PlatformAuditRead = "platform.audit.read";

    /// <summary>List and view user accounts.</summary>
    public const string IdentityUserRead = "identity.user.read";

    /// <summary>Create accounts, change their details, lock and unlock them.</summary>
    public const string IdentityUserManage = "identity.user.manage";

    /// <summary>Grant and revoke roles.</summary>
    public const string IdentityRoleAssign = "identity.role.assign";

    /// <summary>List roles and the permission catalogue.</summary>
    public const string IdentityRoleRead = "identity.role.read";

    /// <summary>Create roles and change what they grant.</summary>
    public const string IdentityRoleManage = "identity.role.manage";

    /// <summary>Every declared permission, in the order the admin UI lists them.</summary>
    public static readonly IReadOnlyList<PermissionDescriptor> All =
    [
        new(PlatformSettingsManage, "Platform", "Change store settings, branding and feature flags."),
        new(PlatformAuditRead, "Platform", "Read the audit trail."),
        new(IdentityUserRead, "Users & access", "List and view user accounts."),
        new(IdentityUserManage, "Users & access", "Create accounts, edit them, lock and unlock them."),
        new(IdentityRoleRead, "Users & access", "List roles and the permissions they grant."),
        new(IdentityRoleManage, "Users & access", "Create roles and change what they grant."),
        new(IdentityRoleAssign, "Users & access", "Grant and revoke a user's roles."),
    ];

    /// <summary>Whether a code is one this platform declares.</summary>
    /// <param name="code">The permission code.</param>
    public static bool Contains(string code)
        => All.Any(permission => string.Equals(permission.Code, code, StringComparison.Ordinal));
}

/// <summary>One role, as the platform defines it.</summary>
/// <param name="Code">The stable lowercase code.</param>
/// <param name="Name">The display name.</param>
/// <param name="Scope">Platform-wide, vendor-scoped, or a shopper.</param>
/// <param name="Description">What the role is for.</param>
/// <param name="Permissions">What it grants.</param>
internal sealed record RoleDescriptor(
    string Code,
    string Name,
    RoleScope Scope,
    string Description,
    IReadOnlyList<string> Permissions);

/// <summary>
/// The roles every deployment starts with, one per actor in docs/02-domain-model.md §1.
/// </summary>
/// <remarks>
/// <para>
/// System roles: their permission set is reasserted on every deploy, so editing one in the admin
/// UI would be silently undone. A deployment that wants a different bundle creates its own role —
/// which is exactly what "roles are data, not code" is for, and why the code path that creates
/// them is a first-class endpoint rather than a migration.
/// </para>
/// <para>
/// Several roles start empty. Operations, finance, merchandising and catalog management have no
/// permissions yet because the modules whose endpoints they would grant do not exist; the steps
/// that build those endpoints fill these bundles in. They are seeded now so that the users an
/// operator creates at Step 7 can already be filed under the right role.
/// </para>
/// </remarks>
internal static class SystemRoles
{
    /// <summary>Full control, including users, roles and settings.</summary>
    public const string PlatformAdmin = "platform-admin";

    /// <summary>Read and limited action, with audited impersonation.</summary>
    public const string Support = "support";

    /// <summary>Taxonomy and moderation.</summary>
    public const string CatalogManager = "catalog-manager";

    /// <summary>Orders, fulfilment and returns.</summary>
    public const string Operations = "operations";

    /// <summary>CMS, promotions and collections.</summary>
    public const string Merchandiser = "merchandiser";

    /// <summary>Settlements, payouts and reconciliation.</summary>
    public const string Finance = "finance";

    /// <summary>The legal owner of a seller account.</summary>
    public const string VendorOwner = "vendor-owner";

    /// <summary>A seller's operating staff.</summary>
    public const string VendorStaff = "vendor-staff";

    /// <summary>A registered shopper.</summary>
    public const string Customer = "customer";

    /// <summary>Every system role.</summary>
    public static readonly IReadOnlyList<RoleDescriptor> All =
    [
        new(
            PlatformAdmin,
            "Platform administrator",
            RoleScope.Platform,
            "Full control, including users, roles and store settings.",
            [.. PermissionCatalog.All.Select(permission => permission.Code)]),
        new(
            Support,
            "Support",
            RoleScope.Platform,
            "Reads customer and order data to answer queries; acts only where explicitly permitted.",
            [PermissionCatalog.IdentityUserRead]),
        new(
            CatalogManager,
            "Catalog manager",
            RoleScope.Platform,
            "Owns the taxonomy and moderates listings.",
            []),
        new(
            Operations,
            "Operations",
            RoleScope.Platform,
            "Runs orders, fulfilment and returns.",
            []),
        new(
            Merchandiser,
            "Merchandiser",
            RoleScope.Platform,
            "Owns content, promotions and collections.",
            []),
        new(
            Finance,
            "Finance",
            RoleScope.Platform,
            "Owns settlements, payouts and reconciliation.",
            []),
        new(
            VendorOwner,
            "Vendor owner",
            RoleScope.Vendor,
            "The legal owner of a seller account. Manages that seller's staff and its listings.",
            [PermissionCatalog.IdentityUserRead, PermissionCatalog.IdentityUserManage, PermissionCatalog.IdentityRoleAssign]),
        new(
            VendorStaff,
            "Vendor staff",
            RoleScope.Vendor,
            "Operates one seller's listings and orders.",
            []),
        new(
            Customer,
            "Customer",
            RoleScope.Customer,
            "A registered shopper. Carries no administrative permission at all.",
            []),
    ];
}
