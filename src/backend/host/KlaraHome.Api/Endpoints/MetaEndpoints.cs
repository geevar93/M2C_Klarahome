using KlaraHome.Infrastructure.Configuration;
using KlaraHome.Infrastructure.Observability;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.Options;

namespace KlaraHome.Api.Endpoints;

/// <summary>
/// Deployment metadata. Used to confirm which build is live after a deploy, and as the smoke
/// test that the versioned API surface is reachable end to end.
/// </summary>
internal static class MetaEndpoints
{
    public static IEndpointRouteBuilder MapMetaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/meta", (IOptions<ApiOptions> api, IHostEnvironment environment, IClock clock)
                => Results.Ok(new MetaResponse(
                    api.Value.Version,
                    KlaraHomeTelemetry.Version,
                    environment.EnvironmentName,
                    clock.UtcNow)))
            .WithName("metaGet")
            .WithTags("Meta")
            .WithSummary("Returns the API version, build version and server time.")
            .AllowAnonymous()
            .Produces<MetaResponse>();

        return endpoints;
    }
}

/// <summary>Build and environment identity of the running API.</summary>
/// <param name="ApiVersion">The API contract version, e.g. <c>v1</c>.</param>
/// <param name="BuildVersion">Informational version of the deployed build.</param>
/// <param name="Environment">ASP.NET Core environment name.</param>
/// <param name="ServerTimeUtc">Current server time, always UTC.</param>
internal sealed record MetaResponse(
    string ApiVersion,
    string BuildVersion,
    string Environment,
    DateTimeOffset ServerTimeUtc);
