using Application;
using Domain;
using Infrastructure;
using Xunit;

namespace UnitTests;

public sealed class TelemetryTests
{
    [Fact]
    public void SimulationRecordsTelemetry()
    {
        var telemetry = new InMemoryTelemetryStore();
        var service = new PlantService(new Plant(), new InMemoryEventStore(), telemetry);

        service.Advance(1);
        service.Advance(1);

        var samples = telemetry.GetRecent(10);
        Assert.Equal(2, samples.Count);
        Assert.Equal(2, samples[1].Revision);
    }

    [Fact]
    public void AlarmIsRaisedOnlyWhenThresholdIsCrossed()
    {
        var events = new InMemoryEventStore();
        var service = new PlantService(new Plant(), events, new InMemoryTelemetryStore());

        for (var i = 0; i < 5; i++)
            service.Advance(60);

        Assert.Single(events.GetRecent());
        Assert.Equal("ProcessAlarm", events.GetRecent()[0].Category);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public void InvalidSampleCountIsRejected(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryTelemetryStore().GetRecent(count));
    }
}
