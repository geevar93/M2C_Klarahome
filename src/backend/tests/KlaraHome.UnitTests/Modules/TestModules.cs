using KlaraHome.Infrastructure.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.UnitTests.Modules;

/// <summary>
/// Minimal <see cref="IModule"/> for registry tests. Concrete subclasses in this assembly are
/// deliberately discoverable, so the scanning path is exercised against real types.
/// </summary>
public abstract class FakeModule(string name, string schema, int order) : IModule
{
    public string Name { get; } = name;

    public string Schema { get; } = schema;

    public int Order { get; } = order;

    public bool ServicesAdded { get; private set; }

    public bool EndpointsMapped { get; private set; }

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ServicesAdded = true;
        services.AddSingleton(new ModuleMarker(Name));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => EndpointsMapped = true;
}

/// <summary>Proof that a module got to register something of its own.</summary>
public sealed record ModuleMarker(string ModuleName);

public sealed class AlphaModule() : FakeModule("Alpha", "alpha", 100);

public sealed class BetaModule() : FakeModule("Beta", "beta", 100);

public sealed class EarlyModule() : FakeModule("Early", "early", 1);
