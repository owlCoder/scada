using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;
using UnitTests.Grid.Support;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Grid;

public sealed class GridHistoryServiceTests
{
    [Fact]
    public async Task EveryStepRecordsTheReportedValuesOfTheRedactedView()
    {
        var rig = new DispatcherGridRig();
        var report = await rig.StepAsync();
        Assert.NotNull(report);
        var redacted = GridViewRedactor.ForOperators(report.Snapshot, rig.Engine.Definition);
        foreach (var measurement in redacted.Measurements)
        {
            var series = rig.History.Series(measurement.Id, 100);
            var point = Assert.Single(series);
            Assert.Equal(measurement.ReportedValue, point.Value);
            Assert.Equal(measurement.Quality, point.Quality);
            Assert.Equal(report.Snapshot.SimulationTime, point.SimulationTime);
            Assert.Equal(report.Timestamp, point.Timestamp);
        }
        Assert.Equal(redacted.Measurements.Count, rig.History.MeasurementIds.Count);

        await rig.StepAsync();
        await rig.StepAsync();
        Assert.All(rig.History.MeasurementIds, id => Assert.Equal(3, rig.History.Series(id, 100).Count));
    }

    [Fact]
    public async Task HistoryEveryStepsRecordsOnlyEveryNthStep()
    {
        var rig = new DispatcherGridRig(new GridOptions { HistoryEverySteps = 5 });
        for (var i = 0; i < 12; i++) await rig.StepAsync();
        // Steps 5 and 10 of 12 satisfy StepCount % 5 == 0.
        Assert.All(rig.History.MeasurementIds, id => Assert.Equal(2, rig.History.Series(id, 100).Count));
    }

    [Fact]
    public async Task RingBufferIsBoundedAt2000PointsPerMeasurement()
    {
        var rig = new DispatcherGridRig(new GridOptions { StepSeconds = 1, HistoryEverySteps = 1 }, GridEngineNetworks.TwoBus());
        for (var i = 0; i < 2050; i++) await rig.StepAsync();
        var id = rig.History.MeasurementIds.First(m => m.EndsWith(":Voltage", StringComparison.Ordinal));
        var full = rig.History.Series(id, 100_000);
        Assert.Equal(GridHistoryService.Capacity, full.Count);
        // Oldest points were dropped, order is chronological and the newest point matches the live reported value.
        Assert.True(full.Select(p => p.SimulationTime).SequenceEqual(full.Select(p => p.SimulationTime).OrderBy(t => t)));
        var reported = GridViewRedactor.ForOperators(rig.Engine.Snapshot(), rig.Engine.Definition)
            .Measurements.Single(m => m.Id == id).ReportedValue;
        Assert.Equal(reported, full[^1].Value);
        var last10 = rig.History.Series(id, 10);
        Assert.Equal(10, last10.Count);
        Assert.Equal(full.Skip(full.Count - 10).Select(p => p.Value), last10.Select(p => p.Value));
    }

    [Fact]
    public async Task HistorianStoresReportedValuesEvenWhenTheMeasurementIsSpoofed()
    {
        var rig = new DispatcherGridRig(new GridOptions { StepSeconds = 1 }, GridEngineNetworks.TwoBus());
        await rig.StepAsync();
        var id = rig.Engine.Snapshot().Measurements.First(m => m.Kind == GridMeasurementKind.Voltage).Id;
        Assert.True((await rig.Training.SpoofMeasurementAsync(id, 999, DispatcherGridRig.Instructor, CancellationToken.None)).Accepted);
        await rig.StepAsync();

        var point = rig.History.Series(id, 100)[^1];
        Assert.Equal(999, point.Value); // what the operator sees (the spoofed reported value) ...
        var truth = rig.Engine.Snapshot().Measurements.Single(m => m.Id == id);
        Assert.NotEqual(truth.TrueValue, point.Value); // ... never the ground truth
        Assert.True(truth.IsSpoofed);
    }

    [Fact]
    public async Task HistoryIsClearedWhenADifferentDefinitionIsActivated()
    {
        var rig = new DispatcherGridRig();
        for (var i = 0; i < 3; i++) await rig.StepAsync();
        var id = rig.History.MeasurementIds.First();
        Assert.Equal(3, rig.History.Series(id, 100).Count);

        var saved = await rig.Store.SaveAsync(DefaultGridDefinition.Create() with { Name = "Historian v2" }, "instructor", "activation", CancellationToken.None);
        Assert.True((await rig.Coordinator.ActivateVersionAsync(saved.Version, DispatcherGridRig.Instructor, CancellationToken.None)).Accepted);
        await rig.StepAsync();

        // The pre-activation points are gone: only post-activation samples remain.
        Assert.True(rig.History.Series(id, 100).Count <= 2);
    }

    [Fact]
    public async Task ReadsAreThreadSafeWhileStepsRecord()
    {
        var rig = new DispatcherGridRig(new GridOptions { StepSeconds = 1 }, GridEngineNetworks.TwoBus());
        await rig.StepAsync();
        var id = rig.History.MeasurementIds.First();
        var stop = 0;
        var stepper = Task.Run(async () =>
        {
            while (Volatile.Read(ref stop) == 0) await rig.StepAsync();
        });
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                _ = rig.History.Series(id, 50).Count;
                _ = rig.History.MeasurementIds.Count;
            }
        })).ToArray();
        await Task.WhenAll(readers);
        Volatile.Write(ref stop, 1);
        await stepper;
        Assert.True(rig.History.Series(id, 50).Count > 0);
    }

    [Fact]
    public async Task UnknownMeasurementAndNonPositivePointsReturnEmpty()
    {
        var rig = new DispatcherGridRig();
        await rig.StepAsync();
        Assert.Empty(rig.History.Series("no:such:measurement", 100));
        var id = rig.History.MeasurementIds.First();
        Assert.Empty(rig.History.Series(id, 0));
        Assert.Empty(rig.History.Series(id, -5));
    }
}
