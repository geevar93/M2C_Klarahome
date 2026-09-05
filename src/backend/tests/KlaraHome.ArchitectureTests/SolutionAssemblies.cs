using System.Reflection;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

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

    /// <summary>
    /// True for a class written by <c>dotnet ef</c>: a migration, or the model snapshot.
    /// </summary>
    /// <remarks>
    /// The scaffolder emits these as <c>public partial</c> and offers no way to change that, and
    /// partial declarations cannot disagree about accessibility. They are exempted from the
    /// "nothing public" rule because they are generated, carry no API a module could consume, and
    /// the alternative is hand-editing every generated file — a step that would be forgotten once
    /// and then quietly never done again. The context itself is covered by its own rule.
    /// </remarks>
    public static bool IsGeneratedMigration(Type type)
        => typeof(Migration).IsAssignableFrom(type) || typeof(ModelSnapshot).IsAssignableFrom(type);

    /// <summary>Every <see cref="DbContext"/> an assembly declares, public or not.</summary>
    public static IReadOnlyList<Type> DbContextTypesIn(Assembly assembly)
        => [.. assembly.GetTypes().Where(type => typeof(DbContext).IsAssignableFrom(type)
                                                 && type is { IsAbstract: false, IsInterface: false })];

    /// <summary>
    /// The schema a context declares, read without a connection: the property is instance-level, so
    /// the context is created uninitialised rather than constructed with provider options it would
    /// only use to connect.
    /// </summary>
    public static string DeclaredSchemaOf(Type contextType)
    {
        var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(contextType);

        return (string)contextType
            .GetProperty(nameof(KlaraHomeDbContext.Schema))!
            .GetValue(instance)!;
    }

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
