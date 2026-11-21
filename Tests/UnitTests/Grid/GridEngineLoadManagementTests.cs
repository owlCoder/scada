using Domain.Grid.Commands;
using Domain.Grid.Definition;
using Domain.Grid.Network;
using Domain.Grid.Snapshots;
using Xunit;

namespace UnitTests.Grid;

public sealed class GridEngineLoadManagementTests
{
    [Fact]
    public Task ShedLoadLosesCustomersAndRaisesLossOfSupplyUntilRestored()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 42, TimeSpan.FromHours(8));
        var before = world.Snapshot().Totals.CustomersSupplied;
        var shed = world.ApplyCommand(new(GridCommandType.ShedLoad, "L11"), "ana", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30));
        Assert.True(shed.Accepted);
        world.Step(1);
        var shedSnapshot = world.Snapshot();
        Assert.True(shedSnapshot.Load("L11")!.Shed);
        Assert.Equal(0, shedSnapshot.Load("L11")!.CustomersSupplied);
        Assert.Equal(before - shedSnapshot.Load("L11")!.Customers, shedSnapshot.Totals.CustomersSupplied);
        Assert.Contains(shedSnapshot.Alarms, a => a.Active && a.Type == GridAlarmType.LossOfSupply && a.SourceId == "L11");
        Assert.True(world.ApplyCommand(new(GridCommandType.RestoreLoad, "L11"), "ana", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30)).Accepted);
        world.Step(1);
        Assert.Equal(before, world.Snapshot().Totals.CustomersSupplied);
        Assert.DoesNotContain(world.Snapshot().Alarms, a => a.Active && a.Type == GridAlarmType.LossOfSupply && a.SourceId == "L11");
        Assert.Equal(GridCommandRejection.UnknownTarget,
            world.ApplyCommand(new(GridCommandType.ShedLoad, "no-such-load"), "ana", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30)).Reason);
        return Task.CompletedTask;
    }

    [Fact]
    public Task CapacitorStepsRaiseBusVoltageAndOutOfRangeStepsAreRejected()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 42, TimeSpan.FromHours(8));
        var idle = world.Snapshot().Bus("MV")!.VoltageKv;
        Assert.Equal(0, world.Snapshot().Capacitors.Single(c => c.Id == "CAP1").StepsInService);
        var rejected = world.ApplyCommand(new(GridCommandType.SetCapacitorSteps, "CAP1", 9), "ana", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30));
        Assert.Equal(GridCommandRejection.InvalidValue, rejected.Reason);
        Assert.True(world.ApplyCommand(new(GridCommandType.SetCapacitorSteps, "CAP1", 4), "ana", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30)).Accepted);
        var compensated = world.Snapshot();
        Assert.Equal(4, compensated.Capacitors.Single(c => c.Id == "CAP1").StepsInService);
        Assert.True(compensated.Capacitors.Single(c => c.Id == "CAP1").ReactivePowerMvar > 2, "Q scales with V^2");
        Assert.True(compensated.Bus("MV")!.VoltageKv > idle + 0.05, $"expected compensation to raise MV voltage, got {compensated.Bus("MV")!.VoltageKv} vs {idle}");
        Assert.True(world.ApplyCommand(new(GridCommandType.SetCapacitorSteps, "CAP1", 0), "ana", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30)).Accepted);
        Assert.Equal(idle, world.Snapshot().Bus("MV")!.VoltageKv, 3);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TapCommandsRequireManualModeAndClampAtTheLimits()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 42, TimeSpan.FromHours(8));
        var initial = world.Taps["TR1"];
        Assert.True(world.TapAutomatic["TR1"]);
        Assert.Equal(GridCommandRejection.Interlocked,
            world.ApplyCommand(new(GridCommandType.TapRaise, "TR1"), "ana", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30)).Reason);
        Assert.True(world.ApplyCommand(new(GridCommandType.SetTapManual, "TR1"), "ana", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30)).Accepted);
        Assert.True(world.ApplyCommand(new(GridCommandType.TapRaise, "TR1"), "ana", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30)).Accepted);
        Assert.Equal(initial + 1, world.Taps["TR1"]);
        Assert.True(world.ApplyCommand(new(GridCommandType.TapLower, "TR1"), "ana", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30)).Accepted);
        Assert.Equal(initial, world.Taps["TR1"]);
        Assert.True(world.SetTap("TR1", 99, "ana", GridChangeCause.Operator).Reason == GridCommandRejection.InvalidValue);
        Assert.Equal(initial, world.Taps["TR1"]);
        Assert.Equal(GridCommandRejection.UnknownTarget,
            world.ApplyCommand(new(GridCommandType.TapRaise, "TS11"), "ana", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30)).Reason);
        return Task.CompletedTask;
    }
}
