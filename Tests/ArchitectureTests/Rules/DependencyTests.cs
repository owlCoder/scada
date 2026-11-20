using Xunit;

namespace ArchitectureTests.Rules;

public sealed class DependencyTests
{
    [Fact]
    public Task DomainDoesNotDependOnOtherModules()
    {
        var references = typeof(Domain.Plant).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain("Application", references);
        Assert.DoesNotContain("Infrastructure", references);
        Assert.DoesNotContain("Security", references);
        Assert.DoesNotContain("Protocols", references);
        Assert.DoesNotContain("Simulation", references);
        Assert.DoesNotContain("Api", references);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ApplicationDoesNotDependOnInfrastructure()
    {
        var references = typeof(Application.PlantService).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain("Infrastructure", references);
        Assert.DoesNotContain("Security", references);
        Assert.DoesNotContain("Protocols", references);
        Assert.DoesNotContain("Simulation", references);
        Assert.DoesNotContain("Api", references);
    }
}
