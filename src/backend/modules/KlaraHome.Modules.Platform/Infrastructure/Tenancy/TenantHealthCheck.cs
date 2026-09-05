using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Platform.Domain;
using KlaraHome.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KlaraHome.Modules.Platform.Infrastructure.Tenancy;

/// <summary>
/// Readiness check: does the tenant this process is configured to write actually exist, and is it
/// serving?
/// </summary>
/// <remarks>
/// <para>
/// The ambient tenant id comes from configuration, and when <c>Tenant:Id</c> is left unset it is
/// derived from <c>Tenant:Code</c>. Changing the code therefore changes the id — and every row
/// already written keeps the old one. Nothing would fail: the global query filter would simply
/// return an empty result for every table, and the deployment would look like a brand new,
/// slightly haunted install.
/// </para>
/// <para>
/// This check turns that silent failure into a readiness failure, which is what it actually is. A
/// replica that would serve an empty catalogue is not ready, and the orchestrator keeps traffic on
/// the version that is.
/// </para>
/// </remarks>
/// <param name="db">The Platform module's context.</param>
/// <param name="tenant">The ambient tenant, as configured.</param>
internal sealed class TenantHealthCheck(PlatformDbContext db, ITenantContext tenant) : IHealthCheck
{
    /// <summary>Name the check is registered and reported under.</summary>
    public const string Name = "platform-tenant";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["tenantId"] = tenant.TenantId,
            ["tenantCode"] = tenant.Code,
        };

        var found = await db.Tenants
            .AsNoTracking()
            .Where(candidate => candidate.Id == tenant.TenantId)
            .Select(candidate => new { candidate.Code, candidate.Status })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (found is null)
        {
            return HealthCheckResult.Unhealthy(
                $"No tenant row for the configured tenant id {tenant.TenantId}. Either the migrator has not "
                + "seeded it yet, or Tenant:Code was changed on a deployment that already holds data, which "
                + "derives a different id and hides every existing row.",
                data: data);
        }

        if (found.Status != TenantStatus.Active)
        {
            return HealthCheckResult.Unhealthy($"Tenant '{found.Code}' is {found.Status}.", data: data);
        }

        if (!string.Equals(found.Code, tenant.Code, StringComparison.Ordinal))
        {
            // Same id, different code: the id was pinned explicitly and the code then edited. The
            // data is intact, so this is a warning rather than a refusal — but the two must be
            // reconciled before anyone reads a log and believes the code.
            return HealthCheckResult.Degraded(
                $"The configured tenant code is '{tenant.Code}' but the tenant row says '{found.Code}'.",
                data: data);
        }

        return HealthCheckResult.Healthy($"Tenant '{found.Code}' is active.", data);
    }
}
