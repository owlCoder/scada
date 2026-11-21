using Domain.Control;
using Domain.Process.Devices;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Domain.Control;

public sealed class ControllerTests
{
    [Fact]
    public async Task PlcStartsTheDutyPumpBelowTheStartLevelAndStopsAboveTheStopLevel()
    {
        var rig = new TestRig();
        rig.Training.SetTankLevel("T-102", 30);

        await rig.RunForAsync(10);
        var filling = rig.Training.Snapshot();
        Assert.Equal(PumpState.Running, filling.Pump("P-101")!.State);
        Assert.Equal(PumpState.Stopped, filling.Pump("P-103")!.State);

        rig.Training.SetTankLevel("T-102", 80);
        await rig.RunForAsync(10);
        Assert.Equal(PumpState.Stopped, rig.Training.Snapshot().Pump("P-101")!.State);
    }

    [Fact]
    public async Task SetpointRegistersDriveTheControlLoop()
    {
        var rig = new TestRig();
        rig.Training.SetTankLevel("T-102", 60);
        await rig.RunForAsync(5);
        Assert.Equal(PumpState.Stopped, rig.Training.Snapshot().Pump("P-101")!.State);

        Assert.True(rig.Training.ForceRegister("PLC-1", RegisterArea.Holding, ControllerRegisterMap.SetpointBase, 700));
        await rig.RunForAsync(5);

        Assert.Equal(PumpState.Running, rig.Training.Snapshot().Pump("P-101")!.State);
    }

    [Fact]
    public async Task LossOfFieldCommunicationLatchesSafeStateUntilReset()
    {
        var rig = new TestRig();
        Assert.True(rig.Training.SetLinkConnected("L-PLC1-IO", false));
        await rig.RunForAsync(15);
        Assert.Equal(ControllerState.SafeState, Plc1(rig).State);

        rig.Training.SetLinkConnected("L-PLC1-IO", true);
        await rig.RunForAsync(10);
        Assert.Equal(ControllerState.SafeState, Plc1(rig).State);

        Assert.True(rig.Training.ResetController("PLC-1"));
        await rig.RunForAsync(5);
        var snapshot = Plc1(rig);
        Assert.Equal(ControllerState.Running, snapshot.State);
        Assert.Equal(CommunicationState.Online, snapshot.FieldCommunication);
    }

    [Fact]
    public async Task LooplessOutputsReturnToTheirAutomaticPositionAfterFailSafe()
    {
        var rig = new TestRig();
        rig.Training.SetLinkConnected("L-PLC1-IO", false);
        await rig.RunForAsync(15);
        Assert.Equal(0, rig.Training.Snapshot().Valve("V-102")!.Setpoint);

        rig.Training.SetLinkConnected("L-PLC1-IO", true);
        rig.Training.ResetController("PLC-1");
        await rig.RunForAsync(10);

        Assert.Equal(100, rig.Training.Snapshot().Valve("V-102")!.Position);
        Assert.Equal(100, rig.Training.Snapshot().Valve("V-103")!.Position);
    }

    [Fact]
    public async Task OutletReturnsToItsAutomaticPositionWhenTheOperatorHandsBack()
    {
        var rig = new TestRig();
        var ana = new global::Application.Identity.Actor("ana", global::Application.Identity.AccessRole.Operator);
        rig.Commands.Execute(new global::Application.Commands.OperatorCommand("V-301", global::Application.Commands.OperatorCommandType.SetManual,
            null, global::Application.Commands.CommandSource.Hmi), ana);
        rig.Commands.Execute(new global::Application.Commands.OperatorCommand("V-301", global::Application.Commands.OperatorCommandType.SetValvePosition,
            20, global::Application.Commands.CommandSource.Hmi), ana);
        await rig.RunForAsync(10);
        Assert.Equal(20, rig.Training.Snapshot().Valve("V-301")!.Position);

        rig.Commands.Execute(new global::Application.Commands.OperatorCommand("V-301", global::Application.Commands.OperatorCommandType.SetAutomatic,
            null, global::Application.Commands.CommandSource.Hmi), ana);
        await rig.RunForAsync(10);

        Assert.Equal(70, rig.Training.Snapshot().Valve("V-301")!.Setpoint);
    }

    [Fact]
    public async Task StatusRegistersPublishControllerState()
    {
        var rig = new TestRig();
        await rig.RunForAsync(5);
        rig.Training.SetControllerRunning("PLC-2", false);
        await rig.RunForAsync(2);

        var read = rig.Registers.Read(2, RegisterArea.Holding, ControllerRegisterMap.ControllerState, 1, "test");
        Assert.Equal((ushort)ControllerState.Stopped, read.Values[0]);
        var scans = rig.Registers.Read(1, RegisterArea.Holding, ControllerRegisterMap.ScanCounter, 1, "test");
        Assert.True(scans.Values[0] > 0);
    }

    private static ControllerSnapshot Plc1(TestRig rig) =>
        rig.Engine.Read(world => world.FindController("PLC-1")!.Snapshot());
}
