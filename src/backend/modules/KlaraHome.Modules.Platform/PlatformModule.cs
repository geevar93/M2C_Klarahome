using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Platform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Platform;

/// <summary>
/// Tenancy, platform settings, branding and audit.
/// </summary>
/// <remarks>
/// Step 3 registered the module so the discovery convention was exercised end to end. Step 4 adds
/// its <c>DbContext</c> — which also carries the outbox and inbox tables, since those live in this
/// module's schema. The Platform feature set itself is Step 6; nothing here may be treated as a
/// placeholder for another module.
/// </remarks>
public sealed class PlatformModule : IModule
{
    /// <summary>
    /// The Postgres schema this module owns, as a constant so the context and its design-time
    /// factory name it without constructing the module.
    /// </summary>
    public const string SchemaName = "platform";

    /// <inheritdoc />
    public string Name => "Platform";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// Registered first: tenancy and settings are what every other module reads its
    /// configuration from, and this context carries the outbox every other module writes to.
    /// </summary>
    public int Order => 10;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<PlatformDbContext>(configuration, this);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
    }
}
