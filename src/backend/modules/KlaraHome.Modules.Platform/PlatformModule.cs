using KlaraHome.Infrastructure.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Platform;

/// <summary>
/// Tenancy, platform settings, branding and audit.
/// </summary>
/// <remarks>
/// Step 3 registers the module so the discovery convention is exercised end to end; it has no
/// services and no endpoints yet. The feature set is built in Step 6 — see the step card in
/// docs/IMPLEMENTATION_PLAN.md. Nothing here may be treated as a placeholder for another module.
/// </remarks>
public sealed class PlatformModule : IModule
{
    /// <inheritdoc />
    public string Name => "Platform";

    /// <inheritdoc />
    public string Schema => "platform";

    /// <summary>
    /// Registered first: tenancy and settings are what every other module reads its
    /// configuration from.
    /// </summary>
    public int Order => 10;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
    }
}
