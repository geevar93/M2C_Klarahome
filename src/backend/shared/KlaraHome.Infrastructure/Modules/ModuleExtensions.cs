using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Infrastructure.Modules;

/// <summary>Host-side glue for the module convention.</summary>
public static partial class ModuleExtensions
{
    /// <summary>
    /// Discovers the modules in <paramref name="moduleAssemblies"/>, registers the registry
    /// itself, and lets every module register its own services.
    /// </summary>
    public static ModuleRegistry AddModules(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] moduleAssemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var registry = ModuleRegistry.FromAssemblies(moduleAssemblies);
        services.AddSingleton(registry);

        foreach (var module in registry.Modules)
        {
            module.AddServices(services, configuration);
        }

        return registry;
    }

    /// <summary>
    /// Maps every module beneath the versioned API prefix. Each module receives a group rather
    /// than the raw route builder, so no module can escape <c>/api/v1</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapModules(this WebApplication app, string apiPrefix = "/api/v1")
    {
        ArgumentNullException.ThrowIfNull(app);

        var registry = app.Services.GetRequiredService<ModuleRegistry>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("KlaraHome.Modules");
        var apiGroup = app.MapGroup(apiPrefix);

        foreach (var module in registry.Modules)
        {
            module.MapEndpoints(apiGroup);
            ModuleRegistered(logger, module.Name, module.Schema, module.Order);
        }

        ModulesMapped(logger, registry.Modules.Count, apiPrefix);
        return apiGroup;
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Information,
        Message = "Module {ModuleName} registered (schema {ModuleSchema}, order {ModuleOrder})")]
    private static partial void ModuleRegistered(ILogger logger, string moduleName, string moduleSchema, int moduleOrder);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information,
        Message = "{ModuleCount} module(s) mapped under {ApiPrefix}")]
    private static partial void ModulesMapped(ILogger logger, int moduleCount, string apiPrefix);
}
