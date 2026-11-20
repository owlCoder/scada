using Application;
using Domain;
using Infrastructure;
using Xunit;

namespace UnitTests.Application;

public sealed class TelemetryTests
{
    [Fact]
    public Task SimulationRecordsTelemetry()
    {
        var telemetry = new InMemoryTelemetryStore();
        var service = new PlantService(new Plant(), new InMemoryEventStore(), telemetry);

        service.Advance(1);
        service.Advance(1);

        var samples = telemetry.GetRecent(10);
        Assert.Equal(2, samples.Count);
        Assert.Equal(2, samples[1].Revision);
        return Task.CompletedTask;
    }

    [Fact]
    public Task AlarmIsRaisedOnlyWhenThresholdIsCrossed()
    {
        var events = new InMemoryEventStore();
        var service = new PlantService(new Plant(), events, new InMemoryTelemetryStore());

        for (var i = 0; i < 5; i++)
            service.Advance(60);

        Assert.Single(events.GetRecent());
        Assert.Equal("ProcessAlarm", events.GetRecent()[0].Category);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RecoveryProducesOneNormalStateEvent()
    {
        var events = new InMemoryEventStore();
        var service = new PlantService(new Plant(), events, new InMemoryTelemetryStore());

        for (var i = 0; i < 5; i++)
            service.Advance(60);

        service.SetPump(true, "operator");
        service.Advance(60);
        service.Advance(60);

        var alarms = events.GetRecent().Where(e => e.Category == "ProcessAlarm").ToArray();
        Assert.Equal(2, alarms.Length);
        Assert.Contains(alarms, e => e.Message == "Tank level returned to normal");
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public Task InvalidSampleCountIsRejected(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryTelemetryStore().GetRecent(count));
        return Task.CompletedTask;
    }
}
