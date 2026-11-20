using Application;
using Infrastructure;
using Npgsql;
using Xunit;

namespace IntegrationTests;

public sealed class PostgresStoreTests
{
    [Fact]
    public void TelemetryAndEventsSurviveNewStoreInstances()
    {
        var connectionString = Environment.GetEnvironmentVariable("SCADA_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

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
    }
}
