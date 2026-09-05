using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Infrastructure.Modules;

/// <summary>
/// The contract every module implements so the host stays a pure composition root: the host
/// discovers modules, it never knows what any of them contain.
/// </summary>
/// <remarks>
/// One implementation per module assembly, named <c>&lt;Module&gt;Module</c> and placed at the
/// assembly root (docs/01-architecture.md §4.2). Modules are ordered by <see cref="Order"/>
/// only where registration genuinely depends on it; the default is alphabetical stability.
/// </remarks>
public interface IModule
{
    /// <summary>Short, stable module name. Used in logs, metrics and the OpenAPI tag.</summary>
    string Name { get; }

    /// <summary>
    /// The Postgres schema this module owns. No other module may read or write it, and no
    /// foreign key may cross it (docs/01-architecture.md §2.1). Consumed from Step 4.
    /// </summary>
    string Schema { get; }

    /// <summary>Relative registration order. Lower runs first. Defaults to 100.</summary>
    int Order => 100;

    /// <summary>Registers the module's own services. Must not touch another module's types.</summary>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps the module's endpoints beneath the versioned API group it is handed.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
