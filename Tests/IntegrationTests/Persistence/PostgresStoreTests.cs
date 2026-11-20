using Application;
using Infrastructure;
using Npgsql;
using Xunit;

namespace IntegrationTests.Persistence;

public sealed class PostgresStoreTests
{
    [Fact]
    public async Task DatabaseSchemaIsReady()
    {
        var connectionString = Environment.GetEnvironmentVariable("SCADA_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var source = NpgsqlDataSource.Create(connectionString);
        await new PostgresSchemaService(source).VerifyAsync();
    }

    [Fact]
    public Task IncidentStatusSurvivesNewStoreInstances()
    {
        var connectionString = Environment.GetEnvironmentVariable("SCADA_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
            return Task.CompletedTask;

        using var source = NpgsqlDataSource.Create(connectionString);
        var first = new IncidentService(new PostgresIncidentStore(source));
        var uniqueEvent = new SecurityEvent(DateTimeOffset.UtcNow, "SecurityAlert",
            $"Test {Guid.NewGuid():N}", "integration");
        first.Observe([uniqueEvent]);
        var incident = Assert.Single(first.GetAll(), x => x.Description == uniqueEvent.Message);

        Assert.True(first.TryUpdate(incident.Id, IncidentStatus.Acknowledged, "tester", out _));

        var second = new IncidentService(new PostgresIncidentStore(source));
        var restored = Assert.Single(second.GetAll(), x => x.Id == incident.Id);
        Assert.Equal(IncidentStatus.Acknowledged, restored.Status);

        second.Observe([uniqueEvent]);
        Assert.Single(second.GetAll(), x => x.Id == incident.Id);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TelemetryAndEventsSurviveNewStoreInstances()
    {
        var connectionString = Environment.GetEnvironmentVariable("SCADA_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
            return Task.CompletedTask;

        using var source = NpgsqlDataSource.Create(connectionString);
        var telemetry = new PostgresTelemetryStore(source);
        var events = new PostgresEventStore(source);
        var marker = $"test-{Guid.NewGuid():N}";
        var sample = new TelemetrySample(DateTimeOffset.UtcNow, 42, true, -1);

        telemetry.Append(sample);
        events.Append(new(DateTimeOffset.UtcNow, "IntegrationTest", marker, "test"));

        var otherTelemetry = new PostgresTelemetryStore(source);
        var otherEvents = new PostgresEventStore(source);

        Assert.Contains(otherTelemetry.GetRecent(3600), item => item.Revision == -1);
        Assert.Contains(otherEvents.GetRecent(), item => item.Message == marker);
        return Task.CompletedTask;
    }
}
