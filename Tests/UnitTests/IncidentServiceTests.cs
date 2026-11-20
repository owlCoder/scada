using Application;
using Infrastructure;
using Xunit;

namespace UnitTests;

public sealed class IncidentServiceTests
{
    [Fact]
    public void SecurityAlertCreatesOnlyOneIncident()
    {
        var service = new IncidentService(new InMemoryIncidentStore());
        var alert = new SecurityEvent(DateTimeOffset.UtcNow, "SecurityAlert", "Покушај", "operator");

        service.Observe([alert]);
        service.Observe([alert]);

        Assert.Single(service.GetAll());
        Assert.Equal(IncidentStatus.Open, service.GetAll()[0].Status);
    }

    [Fact]
    public void IncidentCanBeAcknowledgedAndResolved()
    {
        var service = new IncidentService(new InMemoryIncidentStore());
        service.Observe([new(DateTimeOffset.UtcNow, "SafetyInterlock", "Пумпа", "simulation")]);
        var id = service.GetAll()[0].Id;

        Assert.True(service.TryUpdate(id, IncidentStatus.Acknowledged, "operator", out _));
        Assert.True(service.TryUpdate(id, IncidentStatus.Resolved, "admin", out var incident));
        Assert.Equal(IncidentStatus.Resolved, incident!.Status);
        Assert.Throws<InvalidOperationException>(() =>
            service.TryUpdate(id, IncidentStatus.Open, "operator", out _));
    }

    [Fact]
    public void NewServiceInstanceKeepsIncidentsAndStatuses()
    {
        var store = new InMemoryIncidentStore();
        var first = new IncidentService(store);
        var alert = new SecurityEvent(DateTimeOffset.UtcNow, "SecurityAlert", "Провера", "operator");
        first.Observe([alert]);
        var id = Assert.Single(first.GetAll()).Id;
        Assert.True(first.TryUpdate(id, IncidentStatus.Resolved, "admin", out _));

        var second = new IncidentService(store);
        second.Observe([alert]);
        var incident = Assert.Single(second.GetAll());
        Assert.Equal(id, incident.Id);
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
    }

    [Fact]
    public void AuditEventsDoNotCreateIncidents()
    {
        var service = new IncidentService(new InMemoryIncidentStore());
        service.Observe([new(DateTimeOffset.UtcNow, "Audit", "Команда", "operator")]);
        Assert.Empty(service.GetAll());
    }
}
