using Application;
using Infrastructure;
using Xunit;

namespace UnitTests.Application;

public sealed class IncidentObservationResultTests
{
    [Fact]
    public Task ObservationReturnsNumberOfNewIncidents()
    {
        var store = new InMemoryIncidentStore();
        var service = new IncidentService(store);
        var alert = new SecurityEvent(DateTimeOffset.UtcNow, "SecurityAlert", "Test alert", "operator");

        Assert.Equal(1, service.Observe([alert]));
        Assert.Equal(0, service.Observe([alert]));
        Assert.Equal(0, service.Observe([new SecurityEvent(DateTimeOffset.UtcNow,
            "Audit", "Normal command", "operator")]));
        Assert.Single(store.GetAll());
        return Task.CompletedTask;
    }
}
