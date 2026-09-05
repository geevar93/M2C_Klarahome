using NetArchTest.Rules;

namespace KlaraHome.ArchitectureTests;

/// <summary>
/// Keeps the shared projects in their intended layers. SharedKernel is the innermost ring and
/// must stay usable from a module domain layer, which means it can depend on nothing.
/// </summary>
public sealed class SharedLayerTests
{
    [Fact]
    public void SharedKernel_depends_on_nothing_but_the_base_class_library()
    {
        var dependencies = SolutionAssemblies.SharedKernel
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                           && !string.Equals(name, "netstandard", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            dependencies.Count == 0,
            "SharedKernel is referenced by every domain layer, so it must carry no framework or package weight. "
            + $"Found: {string.Join(", ", dependencies)}");
    }

    [Fact]
    public void Contracts_depends_on_nothing_inside_the_solution_except_SharedKernel()
    {
        // A subset, not an equality: the compiler drops a project reference the assembly does not
        // actually use, so the set is empty until a contract first names a SharedKernel type.
        var dependencies = SolutionAssemblies.Contracts
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .Where(name => name.StartsWith("KlaraHome", StringComparison.Ordinal))
            .ToList();

        Assert.All(dependencies, dependency => Assert.Equal("KlaraHome.SharedKernel", dependency));
    }

    [Fact]
    public void Contracts_carries_no_implementation()
    {
        var result = Types.InAssembly(SolutionAssemblies.Contracts)
            .That().ArePublic()
            .And().AreClasses()
            .Should().BeAbstract()
            .Or().BeSealed()
            .GetResult();

        var failing = result.FailingTypeNames ?? [];

        Assert.True(
            !failing.Any(),
            "Cross-module contracts are interfaces and records, never open implementations. "
            + $"Offending types: {string.Join(", ", failing)}");
    }

    [Fact]
    public void Infrastructure_never_depends_on_a_module()
    {
        var dependencies = SolutionAssemblies.Infrastructure
            .GetReferencedAssemblies()
            .Where(SolutionAssemblies.IsModule)
            .Select(assembly => assembly.Name!)
            .ToList();

        Assert.True(
            dependencies.Count == 0,
            $"Cross-cutting infrastructure cannot know about any feature. Found: {string.Join(", ", dependencies)}");
    }

    [Fact]
    public void The_api_host_carries_no_business_logic_of_its_own()
    {
        var result = Types.InAssembly(SolutionAssemblies.Api)
            .That().ResideInNamespaceContaining("KlaraHome.Api")
            .And().DoNotResideInNamespaceContaining("Diagnostics")
            .Should().NotHaveDependencyOn("KlaraHome.Api.Diagnostics")
            .GetResult();

        var failing = result.FailingTypeNames ?? [];

        Assert.True(
            !failing.Any(),
            "The host composes modules; the Development-only diagnostics surface must stay self-contained. "
            + $"Offending types: {string.Join(", ", failing)}");
    }
}
