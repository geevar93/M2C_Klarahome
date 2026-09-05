using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.Modules.Identity.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Infrastructure.Access;

/// <summary>What a user may do, resolved from the roles they hold.</summary>
/// <param name="RoleCodes">The roles held, for the account screen and the 2FA rule.</param>
/// <param name="Permissions">Every permission those roles grant, de-duplicated and sorted.</param>
/// <param name="VendorId">
/// The seller this user acts for, or null. Null for platform staff and customers; a vendor user
/// with grants in more than one seller is a data error, not a supported case — see the remarks on
/// <see cref="AccessResolver"/>.
/// </param>
/// <param name="RequiresTwoFactor">Whether one of the roles makes a second factor mandatory.</param>
internal sealed record UserAccess(
    IReadOnlyList<string> RoleCodes,
    IReadOnlyList<string> Permissions,
    Guid? VendorId,
    bool RequiresTwoFactor);

/// <summary>
/// Turns a user's role grants into the claim set their access token carries.
/// </summary>
/// <remarks>
/// <para>
/// Resolved at sign-in and again at every refresh, never cached. A role taken away therefore takes
/// effect within one access-token lifetime at worst, and immediately for anything that also ends
/// the session.
/// </para>
/// <para>
/// A vendor user is confined to one seller. The token carries a single <c>vendor_id</c>, and the
/// data-layer filter reads that one value — a user with grants in two sellers would silently see
/// only the first, so the resolver takes the lowest id deterministically rather than picking
/// whichever the database returned first. Supporting one person across two sellers means a second
/// account, which is also what an access review expects to find.
/// </para>
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="options">Supplies the roles for which a second factor is mandatory.</param>
internal sealed class AccessResolver(IdentityDbContext context, IOptions<AuthOptions> options)
{
    /// <summary>Resolves what a user may do right now.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UserAccess> ResolveAsync(Guid userId, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters for the vendor filter only: resolving a caller's own scope is the one
        // read that has to happen before that scope is known, and applying the filter here would
        // make a vendor user's grants invisible to the code that is trying to discover them. The
        // tenant filter still applies, because the tenant is ambient and never in question.
        var grants = await context.UserRoles
            .IgnoreQueryFilters([ModelConventions.VendorFilter])
            .Where(grant => grant.UserId == userId)
            .Join(
                context.Roles,
                grant => grant.RoleId,
                role => role.Id,
                (grant, role) => new { role.Code, grant.VendorId, role.Id })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (grants.Count == 0)
        {
            return new UserAccess([], [], null, RequiresTwoFactor: false);
        }

        var roleIds = grants.Select(grant => grant.Id).Distinct().ToList();

        var permissions = await context.RolePermissions
            .Where(permission => roleIds.Contains(permission.RoleId))
            .Select(permission => permission.PermissionCode)
            .Distinct()
            .OrderBy(code => code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var roleCodes = grants
            .Select(grant => grant.Code)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        var vendorId = grants
            .Where(grant => grant.VendorId is not null)
            .Select(grant => grant.VendorId!.Value)
            .OrderBy(id => id)
            .Cast<Guid?>()
            .FirstOrDefault();

        var mandatory = options.Value.MandatoryTwoFactorRoles;
        var requiresTwoFactor = roleCodes.Any(code => mandatory.Contains(code, StringComparer.Ordinal));

        return new UserAccess(roleCodes, permissions, vendorId, requiresTwoFactor);
    }

    /// <summary>Finds a system role by its code, so a seeder or a handler can grant it.</summary>
    /// <param name="code">The role code, from <see cref="SystemRoles"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Role?> FindRoleAsync(string code, CancellationToken cancellationToken)
        => context.Roles.FirstOrDefaultAsync(
            role => role.Code == code,
            cancellationToken);
}
