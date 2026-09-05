using KlaraHome.Infrastructure.Modules;
using KlaraHome.Modules.Platform;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.UnitTests.Modules;

public sealed class ModuleRegistryTests
{
    [Fact]
    public void Discovers_the_module_in_a_real_module_assembly()
    {
        var registry = ModuleRegistry.FromAssemblies(typeof(PlatformModule).Assembly);

        Assert.IsType<PlatformModule>(Assert.Single(registry.Modules));
    }

    [Fact]
    public void Orders_by_declared_order_then_name_so_registration_is_deterministic()
    {
        var registry = ModuleRegistry.FromAssemblies(typeof(ModuleRegistryTests).Assembly);

        Assert.Equal(["Early", "Alpha", "Beta"], registry.Modules.Select(module => module.Name));
    }

    [Fact]
    public void Ignores_a_repeated_assembly_rather_than_registering_a_module_twice()
    {
        var assembly = typeof(PlatformModule).Assembly;

        Assert.Single(ModuleRegistry.FromAssemblies(assembly, assembly).Modules);
    }

    [Fact]
    public void Refuses_two_modules_that_claim_the_same_postgres_schema()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ModuleRegistry.FromModules([new StubModule("First", "shared"), new StubModule("Second", "shared")]));

        Assert.Contains("Postgres schema", exception.Message, StringComparison.Ordinal);
        Assert.Contains("shared", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_two_modules_that_claim_the_same_name()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ModuleRegistry.FromModules([new StubModule("Same", "one"), new StubModule("Same", "two")]));

        Assert.Contains("name", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Same", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddModules_lets_every_module_register_its_own_services()
    {
        var services = new ServiceCollection();

        var registry = services.AddModules(
            new ConfigurationBuilder().Build(),
            typeof(ModuleRegistryTests).Assembly);

        Assert.All(registry.Modules.OfType<FakeModule>(), module => Assert.True(module.ServicesAdded));

        var registered = services.BuildServiceProvider()
            .GetServices<ModuleMarker>()
            .Select(marker => marker.ModuleName)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["Alpha", "Beta", "Early"], registered);
    }

    /// <summary>Private, so module discovery never sees it; used only through FromModules.</summary>
    private sealed class StubModule(string name, string schema) : IModule
    {
        public string Name { get; } = name;

        public string Schema { get; } = schema;

        public void AddServices(IServiceCollection services, IConfiguration configuration)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
        }
    }
}
