using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// The Identity module's data access, owning the <c>identity</c> schema.
/// </summary>
/// <remarks>
/// It takes an <see cref="ICallerContext"/> as well as the tenant, which the Platform context does
/// not: this is the first schema with a vendor-scoped table, and the query filter that keeps one
/// seller out of another's rows reads the caller's vendor from it.
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller, for the vendor filter.</param>
internal sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => IdentityModule.SchemaName;

    /// <summary>Everyone who can sign in: shoppers, vendor staff and platform staff.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Named bundles of permissions.</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>The permission catalogue, as the admin UI displays it.</summary>
    public DbSet<Permission> Permissions => Set<Permission>();

    /// <summary>What each role grants.</summary>
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    /// <summary>Which roles each user holds, and in which vendor.</summary>
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    /// <summary>Signed-in devices.</summary>
    public DbSet<UserSession> Sessions => Set<UserSession>();

    /// <summary>Refresh tokens and their rotation chains.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>One-time codes and verification links.</summary>
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    /// <summary>Shopper details.</summary>
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();

    /// <summary>Saved delivery and billing addresses.</summary>
    public DbSet<Address> Addresses => Set<Address>();

    /// <summary>Identities at external providers, linked to accounts here.</summary>
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RoleConfiguration());
        modelBuilder.ApplyConfiguration(new PermissionConfiguration());
        modelBuilder.ApplyConfiguration(new RolePermissionConfiguration());
        modelBuilder.ApplyConfiguration(new UserRoleConfiguration());
        modelBuilder.ApplyConfiguration(new UserSessionConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new OtpChallengeConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerProfileConfiguration());
        modelBuilder.ApplyConfiguration(new AddressConfiguration());
        modelBuilder.ApplyConfiguration(new ExternalLoginConfiguration());
    }
}
