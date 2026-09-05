using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.Modules.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Infrastructure.Seeding;

/// <summary>
/// Reconciles <c>identity.permissions</c> with the catalogue declared in code.
/// </summary>
/// <remarks>
/// Upsert, never insert-if-empty: a permission added at Step 12 must appear in a database that has
/// been running since Step 7. Codes no longer in the catalogue are removed, so a retired permission
/// stops appearing as a checkbox that grants nothing — the role rows referencing it are left alone
/// and become visibly stale, which is the outcome an operator can act on.
/// </remarks>
/// <param name="context">The Identity data context.</param>
internal sealed class PermissionSeeder(IdentityDbContext context) : IDataSeeder
{
    /// <inheritdoc />
    public string Name => "Identity.Permissions";

    /// <inheritdoc />
    public int Order => 20;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var existing = await context.Permissions
            .ToDictionaryAsync(permission => permission.Code, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        foreach (var declared in PermissionCatalog.All)
        {
            if (existing.TryGetValue(declared.Code, out var row))
            {
                row.Describe(declared.Group, declared.Description);
            }
            else
            {
                context.Permissions.Add(
                    Permission.Create(declared.Code, declared.Group, declared.Description));
            }
        }

        var retired = existing
            .Where(pair => !PermissionCatalog.Contains(pair.Key))
            .Select(pair => pair.Value)
            .ToList();

        context.Permissions.RemoveRange(retired);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Reconciles the system roles and the permissions they grant.
/// </summary>
/// <remarks>
/// A system role's permission set is reasserted on every deploy, which is the whole reason
/// <see cref="Role.IsSystem"/> exists: the admin UI refuses to edit one, because an edit would be
/// undone by the next deploy without anybody noticing. Roles a deployment defines for itself are
/// never touched here.
/// </remarks>
/// <param name="context">The Identity data context.</param>
internal sealed class SystemRoleSeeder(IdentityDbContext context) : IDataSeeder
{
    /// <inheritdoc />
    public string Name => "Identity.SystemRoles";

    /// <inheritdoc />
    public int Order => 21;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var existing = await context.Roles
            .Include(role => role.Permissions)
            .Where(role => role.IsSystem)
            .ToDictionaryAsync(role => role.Code, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        foreach (var declared in SystemRoles.All)
        {
            if (!existing.TryGetValue(declared.Code, out var role))
            {
                role = Role.Define(
                    declared.Code,
                    declared.Name,
                    declared.Scope,
                    declared.Description,
                    isSystem: true);

                context.Roles.Add(role);
            }
            else
            {
                role.Rename(declared.Name, declared.Description);
            }

            role.SetPermissions(declared.Permissions);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Creates the first platform administrator, so a fresh deployment has somebody who can sign in.
/// </summary>
/// <remarks>
/// <para>
/// Runs only when <c>Auth:Bootstrap:Email</c> and <c>Auth:Bootstrap:Password</c> are both supplied,
/// and only when no platform administrator exists yet. A hard-coded default administrator is the
/// single most reliable way to ship a product with a known password on the internet, so there is
/// none: an operator supplies the credential once, through the same secrets mechanism as everything
/// else, and the seeder does nothing on every deploy after the first.
/// </para>
/// <para>
/// The account is created with a second factor still to enrol. <c>platform-admin</c> is a mandatory
/// two-factor role, so the first sign-in goes straight to enrolment rather than to a session.
/// </para>
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="hasher">Hashes the supplied password.</param>
/// <param name="options">Supplies the bootstrap credential.</param>
/// <param name="environment">Refuses a weak bootstrap password outside Development.</param>
/// <param name="logger">Reports what was created, without the credential.</param>
internal sealed partial class BootstrapAdminSeeder(
    IdentityDbContext context,
    PasswordHasher hasher,
    IOptions<AuthOptions> options,
    IHostEnvironment environment,
    ILogger<BootstrapAdminSeeder> logger) : IDataSeeder
{
    /// <inheritdoc />
    public string Name => "Identity.BootstrapAdmin";

    /// <inheritdoc />
    public int Order => 22;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var bootstrap = options.Value.Bootstrap;

        if (string.IsNullOrWhiteSpace(bootstrap.Email) || string.IsNullOrWhiteSpace(bootstrap.Password))
        {
            return;
        }

        var role = await context.Roles
            .FirstOrDefaultAsync(candidate => candidate.Code == SystemRoles.PlatformAdmin, cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            throw new InvalidOperationException(
                "The platform-admin role is missing. Identity.SystemRoles must run before this seeder.");
        }

        var alreadyAdministered = await context.UserRoles
            .AnyAsync(grant => grant.RoleId == role.Id, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyAdministered)
        {
            return;
        }

        if (bootstrap.Password.Length < options.Value.Password.MinimumLength)
        {
            throw new InvalidOperationException(
                $"Auth:Bootstrap:Password is shorter than the configured minimum of "
                + $"{options.Value.Password.MinimumLength} characters.");
        }

        if (!environment.IsDevelopment() && BootstrapOptions.IsWellKnown(bootstrap.Password))
        {
            throw new InvalidOperationException(
                "Auth:Bootstrap:Password is one of the example values from the documentation. "
                + "Outside Development the first administrator's password must be a real secret.");
        }

        var email = bootstrap.Email.Trim().ToLowerInvariant();

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Email == email, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            user = User.RegisterStaff(UserType.Staff, email);
            context.Users.Add(user);
        }

        user.SetPasswordHash(hasher.Hash(bootstrap.Password));
        user.GrantRole(role.Id);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        BootstrapAdminCreated(logger, email);
    }

    [LoggerMessage(EventId = 1403, Level = LogLevel.Information,
        Message = "Created the first platform administrator ({BootstrapEmail}). It has no second factor yet; "
                  + "the first sign-in enrols one before a session is issued.")]
    private static partial void BootstrapAdminCreated(ILogger logger, string bootstrapEmail);
}
