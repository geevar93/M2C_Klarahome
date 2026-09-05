using KlaraHome.Infrastructure.Authorization;
using Microsoft.AspNetCore.Http;

namespace KlaraHome.Infrastructure.Tenancy;

/// <summary>
/// Reads the acting user from the request principal. Outside a request — the worker, the
/// migrator, a seeder — there is no principal and the operation is correctly unattributed.
/// </summary>
internal sealed class ClaimsUserContext(IHttpContextAccessor accessor) : IUserContext
{
    public Guid? UserId
    {
        get
        {
            var principal = accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            return Guid.TryParse(principal.FindFirst(KlaraHomeClaims.UserId)?.Value, out var id)
                ? id
                : null;
        }
    }
}

/// <summary>The non-HTTP implementation: background hosts act as the system, not as a user.</summary>
internal sealed class SystemUserContext : IUserContext
{
    public Guid? UserId => null;
}
