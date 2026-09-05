using System.Reflection;

namespace KlaraHome.Infrastructure.Modules;

/// <summary>
/// Discovers and holds the <see cref="IModule"/> instances for a host. Built once at startup;
/// the reflection cost is paid exactly once, never per request.
/// </summary>
public sealed class ModuleRegistry
{
    private ModuleRegistry(IReadOnlyList<IModule> modules) => Modules = modules;

    public IReadOnlyList<IModule> Modules { get; }

    /// <summary>Instantiates every public, concrete <see cref="IModule"/> found in the assemblies.</summary>
    /// <exception cref="InvalidOperationException">
    /// A module type has no public parameterless constructor, or two modules claim the same name
    /// or the same Postgres schema — both are boundary violations that must fail at startup.
    /// </exception>
    public static ModuleRegistry FromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return FromModules(assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => typeof(IModule).IsAssignableFrom(type)
                           && type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .Select(Create));
    }

    /// <summary>
    /// Orders and validates an explicit set of modules. The assembly-scanning path funnels through
    /// here, so the boundary rules are enforced in exactly one place.
    /// </summary>
    public static ModuleRegistry FromModules(IEnumerable<IModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var ordered = modules
            .OrderBy(module => module.Order)
            .ThenBy(module => module.Name, StringComparer.Ordinal)
            .ToList();

        EnsureUnique(ordered, module => module.Name, "name");
        EnsureUnique(ordered, module => module.Schema, "Postgres schema");

        return new ModuleRegistry(ordered);
    }

    private static IModule Create(Type type)
        => Activator.CreateInstance(type) as IModule
           ?? throw new InvalidOperationException(
               $"Module '{type.FullName}' must have a public parameterless constructor.");

    private static void EnsureUnique(IEnumerable<IModule> modules, Func<IModule, string> selector, string label)
    {
        var duplicate = modules
            .GroupBy(selector, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Two or more modules declare the {label} '{duplicate.Key}'. Module boundaries must not overlap.");
        }
    }
}
