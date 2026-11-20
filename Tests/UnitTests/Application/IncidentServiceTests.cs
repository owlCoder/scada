using Application;
using Infrastructure;
using Xunit;

namespace UnitTests.Application;

public sealed class IncidentServiceTests
{
    [Fact]
    public Task SecurityAlertCreatesOnlyOneIncident()
    {
        var service = new IncidentService(new InMemoryIncidentStore());
        var alert = new SecurityEvent(DateTimeOffset.UtcNow, "SecurityAlert", "Attempt", "operator");

        service.Observe([alert]);
        service.Observe([alert]);

        Assert.Single(service.GetAll());
        Assert.Equal(IncidentStatus.Open, service.GetAll()[0].Status);
        return Task.CompletedTask;
    }

    [Fact]
    public Task IncidentCanBeAcknowledgedAndResolved()
    {
        var service = new IncidentService(new InMemoryIncidentStore());
        service.Observe([new(DateTimeOffset.UtcNow, "SafetyInterlock", "Pump", "simulation")]);
        var id = service.GetAll()[0].Id;

        Assert.True(service.TryUpdate(id, IncidentStatus.Acknowledged, "operator", out _));
        Assert.True(service.TryUpdate(id, IncidentStatus.Resolved, "admin", out var incident));
        Assert.Equal(IncidentStatus.Resolved, incident!.Status);
        Assert.Throws<InvalidOperationException>(() =>
            service.TryUpdate(id, IncidentStatus.Open, "operator", out _));
        return Task.CompletedTask;
    }

    [Fact]
    public Task NewServiceInstanceKeepsIncidentsAndStatuses()
    {
        var store = new InMemoryIncidentStore();
        var first = new IncidentService(store);
        var alert = new SecurityEvent(DateTimeOffset.UtcNow, "SecurityAlert", "Verification", "operator");
        first.Observe([alert]);
        var id = Assert.Single(first.GetAll()).Id;
        Assert.True(first.TryUpdate(id, IncidentStatus.Resolved, "admin", out _));

        var second = new IncidentService(store);
        second.Observe([alert]);
        var incident = Assert.Single(second.GetAll());
        Assert.Equal(id, incident.Id);
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        return Task.CompletedTask;
    }

    [Fact]
    public Task AuditEventsDoNotCreateIncidents()
    {
        var service = new IncidentService(new InMemoryIncidentStore());
        service.Observe([new(DateTimeOffset.UtcNow, "Audit", "Command", "operator")]);
        Assert.Empty(service.GetAll());
        return Task.CompletedTask;
    }
}
