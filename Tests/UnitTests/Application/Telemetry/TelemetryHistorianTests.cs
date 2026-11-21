using Application.Telemetry;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Application.Telemetry;

public sealed class TelemetryHistorianTests
{
    [Fact]
    public async Task SimulationRecordsAHashChainedHistoryThatVerifies()
    {
        var rig = new TestRig();

        await rig.RunForAsync(10);

        Assert.Contains("LT-101", rig.Historian.Tags());
        var series = rig.Historian.GetSeries("LT-101", 100);
        Assert.Equal(10, series.Count);
        Assert.True(series.Zip(series.Skip(1)).All(p => p.First.SimulationTime < p.Second.SimulationTime));
        Assert.True(rig.Historian.Verify().IsValid);
        var stored = await rig.TelemetryStore.QueryAsync(new TelemetryQuery("LT-101", null, null, 100), CancellationToken.None);
        Assert.Equal(series.Count, stored.Count);
    }

    [Fact]
    public async Task TamperedSampleBreaksTheChainAndIsReported()
    {
        var rig = new TestRig();
        await rig.RunForAsync(10);

        Assert.True(rig.Training.TamperHistorian("FT-301", 0.123));
        var report = rig.Historian.Verify();

        Assert.False(report.IsValid);
        Assert.Equal("FT-301", report.TagId);
        Assert.False(rig.Training.TamperHistorian("NOT-A-TAG", 1));
    }

    [Fact]
    public Task CapacityMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelemetryHistorian(0));
        return Task.CompletedTask;
    }
}
