using Application.Simulation;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Application.Telemetry;

public sealed class PlantViewRedactorTests
{
    [Fact]
    public async Task OperatorsSeeOnlyWhatInstrumentsReport()
    {
        var rig = new TestRig();
        rig.Training.SpoofSensor("LT-102", 12);
        await rig.RunForAsync(1);
        var truth = rig.Training.Snapshot();

        var view = PlantViewRedactor.ForOperators(truth);

        var sensor = view.Sensor("LT-102")!;
        Assert.False(sensor.IsSpoofed);
        Assert.Null(sensor.SpoofedValue);
        Assert.Equal(12, sensor.TrueValue);
        Assert.Equal(12, view.Tank("T-102")!.LevelPercent);
        Assert.NotEqual(12, truth.Tank("T-102")!.LevelPercent);
        Assert.True(truth.Sensor("LT-102")!.IsSpoofed);
    }

    [Fact]
    public Task SimulationLoopPeriodHasAFloor()
    {
        Assert.NotEmpty(new SimulationOptions { StepSeconds = 0.01, TimeScale = 100 }.Validate());
        Assert.Empty(new SimulationOptions().Validate());
        return Task.CompletedTask;
    }
}
