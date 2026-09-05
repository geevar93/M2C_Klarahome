using System.Reflection;
using KlaraHome.Infrastructure.Modules;

namespace KlaraHome.ArchitectureTests;

/// <summary>
/// Locates the assemblies under test from the build output, so adding a module to the solution
/// automatically brings it under the boundary rules — nobody has to remember to list it here.
/// </summary>
internal static class SolutionAssemblies
{
    private const string ModulePrefix = "KlaraHome.Modules.";

    public static readonly Assembly SharedKernel = typeof(SharedKernel.Results.Result).Assembly;

    public static readonly Assembly Contracts = typeof(Contracts.ContractsAssembly).Assembly;

    public static readonly Assembly Infrastructure = typeof(Infrastructure.InfrastructureExtensions).Assembly;

    public static readonly Assembly Api = typeof(Program).Assembly;

    /// <summary>Every module assembly present in the build output.</summary>
    public static IReadOnlyList<Assembly> Modules { get; } = LoadModules();

    public static bool IsModule(AssemblyName name)
        => name.Name?.StartsWith(ModulePrefix, StringComparison.Ordinal) == true;

    /// <summary>True for a concrete <see cref="IModule"/> implementation.</summary>
    public static bool IsModuleType(Type type)
        => typeof(IModule).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false };

    /// <summary>The module types an assembly exports. Normally exactly one.</summary>
    public static IReadOnlyList<Type> ModuleTypesIn(Assembly assembly)
        => [.. assembly.GetExportedTypes().Where(IsModuleType)];

    public static IModule Instantiate(Type moduleType)
        => (IModule)Activator.CreateInstance(moduleType)!;

    private static List<Assembly> LoadModules()
    {
        var directory = Path.GetDirectoryName(Api.Location)!;

        var assemblies = Directory
            .EnumerateFiles(directory, ModulePrefix + "*.dll")
            .Select(Assembly.LoadFrom)
            .ToList();

        return assemblies.Count > 0
            ? assemblies
            : throw new InvalidOperationException(
                $"No module assemblies were found in '{directory}'. The architecture rules would pass vacuously.");
    }
}
