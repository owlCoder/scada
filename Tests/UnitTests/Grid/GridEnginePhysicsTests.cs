using Application.Grid;
using Domain.Grid.Commands;
using Domain.Grid.Definition;
using Domain.Grid.Network;
using Domain.Grid.PowerFlow;
using Domain.Grid.Snapshots;
using Tests = UnitTests.Support.GridEngineNetworks;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Grid;

public sealed class GridEnginePhysicsTests
{
    [Theory]
    [InlineData("08:00")]
    [InlineData("19:00")]
    [InlineData("12:00")]
    public Task DefaultNetworkConvergesWithinLimitsAndHasNoAlarms(string start)
    {
        var engine = new GridEngine(new() { StartTimeOfDay = start }, TimeProvider.System);
        for (var i = 0; i < 10; i++) engine.Step();
        var s = engine.Snapshot();
        Assert.True(s.Converged);
        Assert.InRange(s.SolverIterations, 1, 30);
        Assert.All(s.Buses, b => Assert.InRange(b.VoltagePu, 0.95, 1.05));
        Assert.All(s.Branches, b => Assert.InRange(b.LoadingPercent, 0, 100));
        Assert.DoesNotContain(s.Alarms, a => a.Active);
        Assert.Equal(s.Totals.Customers, s.Totals.CustomersSupplied);
        Assert.Equal(11, s.Loads.Count);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TwoBusVoltageDropLossesAndPowerBalanceMatchHandCalculation()
    {
        var world = new GridWorld(Tests.TwoBus(), 42, TimeSpan.FromHours(8));
        var s = world.Snapshot();
        Assert.True(s.Converged);
        var p = s.Loads[0].ActivePowerMw; var q = s.Loads[0].ReactivePowerMvar;
        var expectedDropKv = (0.4 * p + 0.2 * q) / 20;
        Assert.InRange(Math.Abs((s.Buses[0].VoltageKv - s.Buses[1].VoltageKv) / expectedDropKv - 1), 0, 0.003);
        var branch = s.Branches[0];
        var expectedLoss = 3 * branch.CurrentA * branch.CurrentA * 0.4 / 1e6;
        Assert.InRange(Math.Abs(branch.LossesMw / expectedLoss - 1), 0, 0.001);
        Assert.InRange(Math.Abs(s.Totals.SourceImportMw - p - s.Totals.LossesMw) / p, 0, 0.001);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TransformerVoltageRatioAndTapPiModelMatchNoLoadCalculation()
    {
        var d = Tests.TwoBus();
        d = d with
        {
            Buses = [d.Buses[0], d.Buses[1] with { NominalVoltageKv = 0.4 }],
            Lines = [],
            Loads = [],
            Switches = [],
            Transformers = [new("TR", "Transformer", "A", "B", 1, 20, 0.4, 6, 10, 0, true, -10, 10, 0, 2, 1.5, 0.4, 1.5, 30, null, new(1, 0))]
        };
        var world = new GridWorld(d, 42, TimeSpan.FromHours(8));
        Assert.True(world.Snapshot().Converged);
        Assert.Equal(0.412, world.Snapshot().Bus("B")!.VoltageKv, 5);
        return Task.CompletedTask;
    }

    [Fact]
    public Task OpenBreakerCreatesZeroVoltageIslandAndTieRestoresMeshedNetwork()
    {
        var engine = new GridEngine(new(), TimeProvider.System);
        engine.Mutate(w => w.OperateSwitch("TIE", true, "instructor", GridChangeCause.Operator));
        Assert.True(engine.Snapshot().Converged);
        engine.Mutate(w => w.OperateSwitch("QF2", false, "instructor", GridChangeCause.Operator));
        var restored = engine.Step()!.Snapshot;
        Assert.True(restored.Converged);
        Assert.True(restored.Bus("F2-4-LV")!.Energized);
        Assert.Equal(restored.Totals.Customers, restored.Totals.CustomersSupplied);
        engine.Mutate(w => w.OperateSwitch("TIE", false, "instructor", GridChangeCause.Operator));
        var island = engine.Step()!.Snapshot;
        Assert.Equal(0, island.Bus("F2-4-LV")!.VoltageKv);
        Assert.False(island.Load("L24")!.Energized);
        Assert.True(island.Totals.CustomersSupplied < island.Totals.Customers);
        return Task.CompletedTask;
    }

    [Fact]
    public Task MeshedNetworkWithTieClosedConvergesAndBalancesPowerWithinTolerance()
    {
        var engine = new GridEngine(new(), TimeProvider.System);
        engine.Mutate(w => w.OperateSwitch("TIE", true, "instructor", GridChangeCause.Operator));
        var s = engine.Step()!.Snapshot;
        Assert.True(s.Converged);
        Assert.Equal(24, s.Buses.Count);
        Assert.All(s.Buses, b => Assert.True(b.Energized));
        var balance = s.Totals.SourceImportMw + s.Totals.GenerationMw - s.Totals.LoadMw - s.Totals.LossesMw;
        Assert.InRange(Math.Abs(balance) / Math.Max(s.Totals.LoadMw, 1e-9), 0, 0.001);
        Assert.True(s.Totals.LossesMw > 0);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ShortCircuitCurrentMatchesSourceAndPathImpedance()
    {
        var d = Tests.TwoBus() with { Loads = [] };
        var world = new GridWorld(d, 1, TimeSpan.Zero);
        Assert.True(world.SetFault("L", GridEquipmentFault.ThreePhaseShortCircuit, "instructor").Accepted);
        var fault = world.Snapshot().Branch("L")!;
        var expected = 20000 / (Math.Sqrt(3) * Math.Sqrt(0.2 * 0.2 + 0.1 * 0.1));
        Assert.InRange(Math.Abs(fault.CurrentA / expected - 1), 0, 0.001);
        return Task.CompletedTask;
    }

    [Fact]
    public Task DivergencePreservesLastGoodStateAndRaisesAlarm()
    {
        var d = DefaultGridDefinition.Create();
        d = d with { Capacitors = [d.Capacitors[0] with { RatedPowerMvar = 1e9 }] };
        var world = new GridWorld(d, 42, TimeSpan.FromHours(8));
        var good = world.Snapshot();
        world.ApplyCommand(new(GridCommandType.SetCapacitorSteps, "CAP1", 4), "operator", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30));
        var failed = world.Snapshot();
        Assert.False(failed.Converged);
        Assert.Equal(good.Bus("MV")!.VoltageKv, failed.Bus("MV")!.VoltageKv);
        Assert.Contains(failed.Alarms, a => a.Type == GridAlarmType.SolverDivergence && a.Active);
        return Task.CompletedTask;
    }

    [Fact]
    public Task AvrWaitsThenRaisesTapTowardTarget()
    {
        var d = DefaultGridDefinition.Create();
        d = d with { Transformers = d.Transformers.Select(t => t.Id == "TR1" ? t with { InitialTap = 0 } : t).ToArray() };
        var world = new GridWorld(d, 42, TimeSpan.FromHours(8));
        for (var i = 0; i < 29; i++) world.Step(1);
        Assert.Equal(0, world.Taps["TR1"]);
        world.Step(1);
        Assert.Equal(1, world.Taps["TR1"]);
        for (var i = 0; i < 30; i++) world.Step(1);
        Assert.InRange(Math.Abs(world.Snapshot().Bus("MV")!.VoltageKv - 20.5), 0, 0.2);
        return Task.CompletedTask;
    }
}
