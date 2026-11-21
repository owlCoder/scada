using Domain.Process;
using Domain.Process.Commands;
using Domain.Process.Configuration;
using Domain.Process.Devices;
using Xunit;

namespace UnitTests.Domain.Process;

public sealed class PlantTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CommandOrigin Operator = CommandOrigin.Operator("operator");

    internal static Plant CreatePlant() => new(DefaultPlantDefinition.Create());

    internal static int Run(Plant plant, double seconds)
    {
        var steps = (int)(seconds / 0.5);
        for (var i = 0; i < steps; i++)
            plant.Step(TimeSpan.FromSeconds(0.5), Now.AddSeconds(i * 0.5));
        return steps;
    }

    [Fact]
    public Task RunningTransferPumpMovesWaterIntoTheTargetTank()
    {
        var plant = CreatePlant();
        var before = plant.CaptureSnapshot().Tank("T-102")!.Volume;

        Assert.True(plant.SetMode("P-101", ControlMode.Manual, Operator).Accepted);
        Assert.True(plant.CommandPump("P-101", true, Operator).Accepted);
        Run(plant, 60);

        var after = plant.CaptureSnapshot();
        Assert.Equal(PumpState.Running, after.Pump("P-101")!.State);
        Assert.True(after.Pump("P-101")!.FlowRate > 0);
        Assert.True(after.Tank("T-102")!.Volume > before + 1, $"T-102 volume {after.Tank("T-102")!.Volume} did not rise from {before}.");
        return Task.CompletedTask;
    }

    [Fact]
    public Task ClosedOutletStopsOutflowFromTheReservoir()
    {
        var plant = CreatePlant();
        Assert.True(plant.SetMode("V-301", ControlMode.Manual, Operator).Accepted);
        Assert.True(plant.CommandValve("V-301", 0, Operator).Accepted);

        Run(plant, 10);

        var valve = plant.CaptureSnapshot().Valve("V-301")!;
        Assert.Equal(0, valve.Position);
        Assert.Equal(0, valve.FlowRate);
        return Task.CompletedTask;
    }

    [Fact]
    public Task OperatorCommandsRequireManualModeAndControllerCommandsRequireAutomatic()
    {
        var plant = CreatePlant();

        Assert.Equal(CommandRejectionReason.ModeMismatch, plant.CommandPump("P-101", true, Operator).Reason);
        Assert.True(plant.SetMode("P-101", ControlMode.Manual, Operator).Accepted);
        Assert.Equal(CommandRejectionReason.ModeMismatch, plant.CommandPump("P-101", true, CommandOrigin.Controller("PLC-1")).Reason);
        Assert.True(plant.CommandPump("P-101", true, Operator).Accepted);
        return Task.CompletedTask;
    }

    [Fact]
    public Task UnattributedChangesBypassModeAndAreAttributed()
    {
        var plant = CreatePlant();

        Assert.True(plant.CommandValve("V-302", 100, CommandOrigin.Unattributed("intruder")).Accepted);
        var step = plant.Step(TimeSpan.FromSeconds(0.5), Now);

        var change = Assert.Single(step.DeviceChanges, c => c.DeviceId == "V-302");
        Assert.Equal(ChangeCause.Unattributed, change.Cause);
        Assert.Equal("intruder", change.Actor);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    [InlineData(100.1)]
    public Task InvalidValvePositionIsRejected(double position)
    {
        var plant = CreatePlant();
        plant.SetMode("V-301", ControlMode.Manual, Operator);
        Assert.Equal(CommandRejectionReason.InvalidValue, plant.CommandValve("V-301", position, Operator).Reason);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(61)]
    public Task InvalidTimeStepIsRejected(double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreatePlant().Step(TimeSpan.FromSeconds(seconds), Now));
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(100.1)]
    [InlineData(double.PositiveInfinity)]
    public Task TankLevelOutsideZeroToHundredIsRejected(double level)
    {
        Assert.Equal(CommandRejectionReason.InvalidValue, CreatePlant().SetTankLevel("T-101", level).Reason);
        return Task.CompletedTask;
    }

    [Fact]
    public Task UnknownDevicesAreRejected()
    {
        var plant = CreatePlant();
        Assert.Equal(CommandRejectionReason.UnknownDevice, plant.CommandPump("P-999", true, Operator).Reason);
        Assert.Equal(CommandRejectionReason.UnknownDevice, plant.SpoofSensor("XX-1", 1).Reason);
        return Task.CompletedTask;
    }

    [Fact]
    public Task SnapshotRestoreReproducesTheProcessState()
    {
        var plant = CreatePlant();
        plant.SetTankLevel("T-103", 72);
        plant.SetMode("V-301", ControlMode.Manual, Operator);
        plant.CommandValve("V-301", 35, Operator);
        Run(plant, 5);
        var snapshot = plant.CaptureSnapshot();

        var restored = Plant.Restore(DefaultPlantDefinition.Create(), snapshot).CaptureSnapshot();

        Assert.Equal(snapshot.SimulationTime, restored.SimulationTime);
        Assert.Equal(snapshot.Tanks.Select(t => Math.Round(t.Volume, 6)), restored.Tanks.Select(t => Math.Round(t.Volume, 6)));
        Assert.Equal(ControlMode.Manual, restored.Valve("V-301")!.Mode);
        Assert.Equal(35, restored.Valve("V-301")!.Setpoint);
        return Task.CompletedTask;
    }

    [Fact]
    public Task SpoofingChangesTheReportedButNotTheTrueValue()
    {
        var plant = CreatePlant();
        var truth = plant.CaptureSnapshot().Sensor("LT-102")!.TrueValue;

        Assert.True(plant.SpoofSensor("LT-102", 12).Accepted);
        Run(plant, 1);

        var sensor = plant.CaptureSnapshot().Sensor("LT-102")!;
        Assert.True(sensor.IsSpoofed);
        Assert.Equal(12, sensor.ReportedValue);
        Assert.Equal(truth, sensor.TrueValue, 3);
        return Task.CompletedTask;
    }
}
