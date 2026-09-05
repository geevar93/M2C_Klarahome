using NetArchTest.Rules;

namespace KlaraHome.ArchitectureTests;

/// <summary>
/// The module boundary rules from docs/01-architecture.md §2.1. These are the rules that keep
/// extraction into a service cheap later; every one of them is cheap to keep and expensive to
/// recover once broken.
/// </summary>
/// <remarks>
/// Each failure message states the rule, not just the violation — a developer who trips one of
/// these should learn why it exists without opening the architecture document.
/// </remarks>
public sealed class ModuleBoundaryTests
{
    [Fact]
    public void No_module_references_another_module()
    {
        var violations = SolutionAssemblies.Modules
            .SelectMany(module => module
                .GetReferencedAssemblies()
                .Where(SolutionAssemblies.IsModule)
                .Select(referenced => $"{module.GetName().Name} -> {referenced.Name}"))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Modules talk through KlaraHome.Contracts or the outbox, never by referencing each other. "
            + $"Found: {string.Join(", ", violations)}");
    }

    [Fact]
    public void No_module_references_a_host()
    {
        var hosts = new[] { "KlaraHome.Api", "KlaraHome.Worker", "KlaraHome.Migrator" };

        var violations = SolutionAssemblies.Modules
            .SelectMany(module => module
                .GetReferencedAssemblies()
                .Where(referenced => hosts.Contains(referenced.Name, StringComparer.Ordinal))
                .Select(referenced => $"{module.GetName().Name} -> {referenced.Name}"))
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"Composition points at modules, never the other way round. Found: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Every_module_assembly_declares_exactly_one_module()
    {
        foreach (var assembly in SolutionAssemblies.Modules)
        {
            var modules = SolutionAssemblies.ModuleTypesIn(assembly);

            Assert.True(
                modules.Count == 1,
                $"{assembly.GetName().Name} must declare exactly one IModule, but declares {modules.Count}.");
        }
    }

    [Fact]
    public void Every_module_can_be_constructed_by_the_registry()
    {
        foreach (var assembly in SolutionAssemblies.Modules)
        {
            var moduleType = SolutionAssemblies.ModuleTypesIn(assembly).Single();

            Assert.True(
                moduleType.GetConstructor(Type.EmptyTypes) is not null,
                $"{moduleType.FullName} needs a public parameterless constructor: "
                + "the module registry instantiates it by reflection at startup.");
        }
    }

    [Fact]
    public void Every_module_owns_a_distinct_postgres_schema()
    {
        var schemas = SolutionAssemblies.Modules
            .SelectMany(SolutionAssemblies.ModuleTypesIn)
            .Select(SolutionAssemblies.Instantiate)
            .Select(module => module.Schema)
            .ToList();

        Assert.Distinct(schemas);
        Assert.All(schemas, schema => Assert.True(
            schema.Length > 0 && string.Equals(schema, schema.ToLowerInvariant(), StringComparison.Ordinal),
            $"Schema '{schema}' must be non-empty and lowercase: it becomes a Postgres identifier."));
    }

    [Fact]
    public void A_module_exposes_nothing_publicly_except_its_module_class()
    {
        foreach (var assembly in SolutionAssemblies.Modules)
        {
            var leaked = assembly.GetExportedTypes()
                .Where(type => !SolutionAssemblies.IsModuleType(type))
                .Select(type => type.FullName)
                .ToList();

            Assert.True(
                leaked.Count == 0,
                "A module's entities, handlers and DTOs stay internal; only KlaraHome.Contracts crosses the "
                + $"boundary. {assembly.GetName().Name} exposes: {string.Join(", ", leaked)}");
        }
    }

    [Fact]
    public void A_module_domain_layer_knows_nothing_about_persistence_or_http()
    {
        foreach (var assembly in SolutionAssemblies.Modules)
        {
            var result = Types.InAssembly(assembly)
                .That().ResideInNamespaceContaining(".Domain")
                .ShouldNot().HaveDependencyOnAny(
                    "Microsoft.EntityFrameworkCore",
                    "Microsoft.AspNetCore",
                    "Npgsql",
                    "StackExchange.Redis")
                .GetResult();

            var failing = result.FailingTypeNames ?? [];

            Assert.True(
                !failing.Any(),
                $"The Domain layer of {assembly.GetName().Name} must stay free of infrastructure. "
                + $"Offending types: {string.Join(", ", failing)}");
        }
    }
}
