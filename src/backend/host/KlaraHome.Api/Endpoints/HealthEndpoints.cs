using KlaraHome.Infrastructure.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace KlaraHome.Api.Endpoints;

/// <summary>
/// Liveness and readiness probes. They sit outside the versioned API surface because they are an
/// operational contract with the orchestrator, not a product API, and they are never versioned.
/// </summary>
internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness: is the process itself worth keeping? A dependency being down must never
        // restart the container — that turns a database blip into a restart loop.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthCheckExtensions.LiveTag),
            ResponseWriter = HealthResponseWriter.WriteAsync,
        })
        .AllowAnonymous()
        .DisableRateLimiting()
        .ExcludeFromDescription();

        // Readiness: can it serve traffic right now? Dependencies included.
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthCheckExtensions.ReadyTag),
            ResponseWriter = HealthResponseWriter.WriteAsync,
        })
        .AllowAnonymous()
        .DisableRateLimiting()
        .ExcludeFromDescription();

        return endpoints;
    }
}
