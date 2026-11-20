using Infrastructure;
using Security;
using Xunit;

namespace UnitTests.Security;

public sealed class ScenarioTests
{
    [Fact]
    public Task KnownScenarioGeneratesSecurityAlert()
    {
        var events = new InMemoryEventStore();
        var scenarios = new ScenarioService(events);

        Assert.True(scenarios.Execute("sensor-spoofing", "instructor"));
        Assert.Contains(events.GetRecent(), e => e.Category == "SecurityAlert");
        return Task.CompletedTask;
    }

    [Fact]
    public Task UnknownScenarioDoesNotGenerateEvent()
    {
        var events = new InMemoryEventStore();
        var scenarios = new ScenarioService(events);

        Assert.False(scenarios.Execute("unknown", "instructor"));
        Assert.Empty(events.GetRecent());
        return Task.CompletedTask;
    }
}
