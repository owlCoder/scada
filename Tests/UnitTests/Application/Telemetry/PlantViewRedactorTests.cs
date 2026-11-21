using Application.Simulation;
using UnitTests.Support;
using Xunit;
using Domain.Process.Commands;
using Domain.Process.Devices;

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
    public Task ActuatorFaultsAndScenarioActorsAreHiddenButSafetyTripsRemainVisible()
    {
        var rig = new TestRig();
        rig.Training.ForceActuator("P-101", 1, "scenario-secret");
        rig.Training.InjectFault(new FaultInjection("P-101", PumpFault: PumpFault.Seized));
        rig.Training.InjectFault(new FaultInjection("V-101", ValveFault: ValveFault.StuckOpen));
        rig.Training.SetInterlockBypass(true, "scenario-secret");
        rig.Engine.Mutate(w => w.Plant.FindPump("P-102")!.Trip("Low-low level in T-102 (dry-run protection)"));
        var truth = rig.Training.Snapshot();
        var view = PlantViewRedactor.ForOperators(truth);
        Assert.Equal(PumpFault.Seized, truth.Pump("P-101")!.Fault);
        Assert.True(truth.InterlocksBypassed);
        Assert.Equal(PumpFault.None, view.Pump("P-101")!.Fault);
        Assert.Equal(DeviceHealth.Healthy, view.Pump("P-101")!.Health);
        Assert.Null(view.Pump("P-101")!.LastChangedBy);
        Assert.Equal(ValveFault.None, view.Valve("V-101")!.Fault);
        Assert.Equal(DeviceHealth.Healthy, view.Valve("V-101")!.Health);
        Assert.False(view.InterlocksBypassed);
        Assert.Equal(truth.Pump("P-102")!.InterlockReason, view.Pump("P-102")!.InterlockReason);
        Assert.Equal(DeviceHealth.Warning, view.Pump("P-102")!.Health);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("High-high level in T-102 (overfill protection)")]
    [InlineData("Low-low level in T-101 (dry-run protection)")]
    [InlineData("Discharge blocked (deadhead protection)")]
    [InlineData("Discharge pressure above trip limit")]
    public Task IndependentSafetyTripsRemainVisibleEvenAlongsideHiddenFaults(string safetyReason)
    {
        var truth = new TestRig().Training.Snapshot();
        truth = truth with
        {
            Pumps = [truth.Pumps[0] with { Fault = PumpFault.Seized, InterlockReason = safetyReason }],
            Valves = [truth.Valves[0] with { Fault = ValveFault.StuckOpen, InterlockReason = "Injected actuator fault: StuckOpen",
                LastChangeCause = ChangeCause.Unattributed, LastChangedBy = "scenario-secret" }]
        };
        var view = PlantViewRedactor.ForOperators(truth);
        Assert.Equal(safetyReason, view.Pumps[0].InterlockReason);
        Assert.Equal(DeviceHealth.Warning, view.Pumps[0].Health);
        Assert.Null(view.Valves[0].InterlockReason);
        Assert.Null(view.Valves[0].LastChangedBy);
        Assert.Equal(DeviceHealth.Healthy, view.Valves[0].Health);
        var diagnosis = truth with { Pumps = [truth.Pumps[0] with { InterlockReason = "Pump seized" }] };
        Assert.Null(PlantViewRedactor.ForOperators(diagnosis).Pumps[0].InterlockReason);
        return Task.CompletedTask;
    }

    [Fact]
    public Task SimulationLoopPeriodHasAFloor()
    {
        Assert.NotEmpty(new SimulationOptions { StepSeconds = 0.01, TimeScale = 100 }.Validate());
        Assert.Empty(new SimulationOptions().Validate());
        return Task.CompletedTask;
    }
}
