using System.Reflection;
using Xunit;

namespace ArchitectureTests.Rules;

/// <summary>Layering: Domain ← Application ← (Security, Protocols, Infrastructure) ← (Simulation, Hmi) ← Api.</summary>
public sealed class DependencyTests
{
    private static readonly string[] Modules = ["Domain", "Application", "Infrastructure", "Security", "Protocols", "Simulation", "Hmi", "Api"];

    public static TheoryData<string, string[]> AllowedDependencies => new()
    {
        { "Domain", [] },
        { "Application", ["Domain"] },
        { "Security", ["Domain", "Application"] },
        { "Protocols", ["Domain", "Application"] },
        { "Infrastructure", ["Domain", "Application"] },
        { "Simulation", ["Domain", "Application", "Security"] },
        { "Hmi", ["Domain", "Application", "Security"] }
    };

    [Theory]
    [MemberData(nameof(AllowedDependencies))]
    public Task ModulesOnlyReferenceLowerLayers(string module, string[] allowed)
    {
        var references = Assembly.Load(module).GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        var forbidden = Modules.Except(allowed).Where(m => m != module).Intersect(references).ToArray();

        Assert.True(forbidden.Length == 0, $"{module} must not reference {string.Join(", ", forbidden)}.");
        return Task.CompletedTask;
    }

    [Fact]
    public Task CoreLayersDoNotDependOnAspNetCoreOrDatabaseDrivers()
    {
        foreach (var module in new[] { "Domain", "Application", "Security", "Simulation" })
        {
            var references = Assembly.Load(module).GetReferencedAssemblies().Select(a => a.Name!).ToArray();
            Assert.DoesNotContain(references, r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
            Assert.DoesNotContain(references, r => r.StartsWith("Npgsql", StringComparison.Ordinal));
            Assert.DoesNotContain(references, r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        }

        return Task.CompletedTask;
    }
}
