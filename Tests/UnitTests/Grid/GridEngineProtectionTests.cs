using Domain.Grid.Definition;
using Domain.Grid.Network;
using Domain.Grid.Protection;
using Domain.Grid.Snapshots;
using Xunit;

namespace UnitTests.Grid;

public sealed class GridEngineProtectionTests
{
    [Theory]
    [InlineData(GridCurveType.IecStandardInverse, 10, 0.1, 0.2970599)]
    [InlineData(GridCurveType.IecVeryInverse, 10, 0.1, 0.15)]
    [InlineData(GridCurveType.IecExtremelyInverse, 10, 0.1, 0.08080808)]
    [InlineData(GridCurveType.DefiniteTime, 10, 0.1, 0.1)]
    public Task CurvesMatchIec60255(GridCurveType curve, double multiple, double setting, double expected)
    {
        Assert.Equal(expected, GridProtectionCurves.OperateSeconds(curve, multiple, setting), 6);
        Assert.True(double.IsPositiveInfinity(GridProtectionCurves.OperateSeconds(curve, 1, setting)));
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(GridEquipmentFault.ThreePhaseShortCircuit)]
    [InlineData(GridEquipmentFault.EarthFault)]
    public Task FaultTripsFeederAndRecloseRetripsPersistentFault(GridEquipmentFault fault)
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 42, TimeSpan.FromHours(8));
        world.SetFault("F1-2", fault, "instructor");
        world.Step(0.04);
        Assert.True(world.Switches["QF1"].Closed);
        world.Step(0.5);
        Assert.False(world.Switches["QF1"].Closed);
        Assert.True(world.Switches["Q0"].Closed);
        Assert.Equal(0, world.Snapshot().Load("L11")!.CustomersSupplied);
        Assert.Contains(world.Snapshot().Alarms, a => a.Type == GridAlarmType.ProtectionTrip && a.Active);
        Assert.True(world.OperateSwitch("QF1", true, "operator", GridChangeCause.Operator).Accepted);
        world.Step(0.5);
        Assert.False(world.Switches["QF1"].Closed);
        return Task.CompletedTask;
    }

    [Fact]
    public Task StuckBreakerIsClearedByDelayedIncomerBackup()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 42, TimeSpan.FromHours(8));
        world.SetFault("QF1", GridEquipmentFault.SwitchStuck, "instructor");
        world.SetFault("F1-1", GridEquipmentFault.ThreePhaseShortCircuit, "instructor");
        world.Step(0.5);
        Assert.True(world.Switches["QF1"].Closed);
        Assert.True(world.Switches["Q0"].Closed);
        Assert.False(world.Relays["QF1-50"].Tripped);
        Assert.True(world.Relays["QF1-50"].Failed);
        Assert.Contains(world.Snapshot().Alarms, a => a.SourceId == "QF1" && a.Type == GridAlarmType.BreakerFailure && a.Active);
        world.Step(1);
        Assert.False(world.Switches["Q0"].Closed);
        Assert.Null(world.Relays["QF1-50"].LastTripAt);
        Assert.Equal(TimeSpan.FromHours(8) + TimeSpan.FromSeconds(1.2), world.Relays["Q0-51"].LastTripAt);
        Assert.Equal(0, world.Snapshot().Totals.CustomersSupplied);
        return Task.CompletedTask;
    }
}
