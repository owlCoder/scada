using Infrastructure;
using Security;
using Xunit;

namespace UnitTests;

public sealed class ScenarioTests
{
    [Fact]
    public void KnownScenarioGeneratesSecurityAlert()
    {
        var events = new InMemoryEventStore();
        var scenarios = new ScenarioService(events);

        Assert.True(scenarios.Execute("sensor-spoofing", "instructor"));
        Assert.Contains(events.GetRecent(), e => e.Category == "SecurityAlert");
    }

    [Fact]
    public void UnknownScenarioDoesNotGenerateEvent()
    {
        var events = new InMemoryEventStore();
        var scenarios = new ScenarioService(events);

        Assert.False(scenarios.Execute("unknown", "instructor"));
        Assert.Empty(events.GetRecent());
    }
}
