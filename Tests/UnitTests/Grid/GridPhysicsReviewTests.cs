using System.Text.Json;
using Application.Grid;
using Domain.Grid.Commands;
using Domain.Grid.Definition;
using Domain.Grid.Definition.Templates;
using Domain.Grid.Network;
using Domain.Grid.PowerFlow;
using Domain.Grid.Profiles;
using Domain.Grid.Protection;
using Domain.Grid.Snapshots;
using Domain.Process.Devices;
using Security.Grid.Scenarios;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Grid;

public sealed class GridPhysicsReviewTests
{
    [Fact]
    public Task ResistanceEarthedFaultUsesResidualAnd51NInsteadOf50()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 42, TimeSpan.FromHours(8));
        world.SetFault("F1-2", GridEquipmentFault.EarthFault, "instructor");
        var flow = new GridPowerFlowSolver().Solve(world);
        Assert.InRange(flow.EarthCurrents["F1-1:MV"], 295, 315);
        Assert.InRange(flow.EarthCurrents["TR1:MV"], 295, 315);
        Assert.Equal(0, flow.EarthCurrents["TR1:HV"]);
        Assert.InRange(world.Snapshot().Branch("F1-1")!.CurrentA, 250, 450);
        world.Step(0.199);
        Assert.True(world.Switches["QF1"].Closed);
        world.Step(0.001);
        Assert.False(world.Switches["QF1"].Closed);
        Assert.True(world.Relays["QF1-51N"].Tripped);
        Assert.False(world.Relays["QF1-50"].Tripped);
        Assert.Equal(TimeSpan.FromHours(8) + TimeSpan.FromSeconds(0.2), world.Relays["QF1-51N"].LastTripAt);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ThreePhaseBusFaultDoesNotCreateAFictitiousEarthContributionAtAnotherFault()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 42, TimeSpan.FromHours(8));
        world.SetFault("MV", GridEquipmentFault.ThreePhaseShortCircuit, "instructor");
        world.SetFault("F1-2", GridEquipmentFault.EarthFault, "instructor");
        var flow = new GridPowerFlowSolver().Solve(world);
        Assert.True(flow.Converged);
        Assert.InRange(flow.EarthCurrents["TR1:MV"], 0, 2);
        return Task.CompletedTask;
    }

    [Fact]
    public Task StuckEarthFaultBreakerRaisesFailureAndNeutralBackupClears()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 42, TimeSpan.FromHours(8));
        world.SetFault("QF1", GridEquipmentFault.SwitchStuck, "instructor");
        world.SetFault("F1-2", GridEquipmentFault.EarthFault, "instructor");
        world.Step(0.4);
        Assert.True(world.Switches["QF1"].Closed);
        Assert.True(world.Switches["Q0"].Closed);
        Assert.False(world.Relays["QF1-51N"].Tripped);
        Assert.Contains(world.DrainEvents(), e => e.Kind == "BreakerFailed" && e.SourceId == "QF1");
        world.Step(0.4);
        Assert.False(world.Switches["Q0"].Closed);
        Assert.True(world.Relays["Q0-51N"].Tripped);
        Assert.Equal(0, world.Snapshot().Branch("F1-1")!.CurrentA);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(GridEquipmentFault.ThreePhaseShortCircuit)]
    [InlineData(GridEquipmentFault.EarthFault)]
    public Task OpenFeederTerminalPreservesTieBackfeedToFault(GridEquipmentFault fault)
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 42, TimeSpan.FromHours(8));
        world.OperateSwitch("TIE", true, "operator", GridChangeCause.Operator);
        world.SetFault("F2-1", fault, "instructor");
        Assert.True(world.OperateSwitch("QF2", false, "operator", GridChangeCause.Operator).Accepted);
        var flow = new GridPowerFlowSolver().Solve(world);
        Assert.True(flow.FaultActive);
        Assert.InRange(world.Snapshot().Branch("F2-1")!.CurrentA, 200, 10000);
        Assert.InRange(world.Snapshot().Branch("F3-1")!.CurrentA, 200, 10000);
        Assert.Equal(0, flow.EarthCurrents.GetValueOrDefault("F2-1:MV"));
        Assert.True(world.OperateSwitch("QF3", false, "operator", GridChangeCause.Operator).Accepted);
        Assert.False(new GridPowerFlowSolver().Solve(world).FaultActive);
        Assert.Equal(0, world.Snapshot().Branch("F2-1")!.CurrentA);
        return Task.CompletedTask;
    }

    [Fact]
    public Task OneEndedHealthyLineRetainsChargingAndOpenTerminalHasNoCurrent()
    {
        var d = GridEngineNetworks.TwoBus() with { Loads = [] };
        d = d with { Lines = [d.Lines[0] with { SusceptanceMicroSiemensPerKm = 90 }] };
        var world = new GridWorld(d, 1, TimeSpan.Zero);
        world.OperateSwitch("Q", false, "operator", GridChangeCause.Operator);
        Assert.Equal(0, world.Snapshot().Branch("L")!.CurrentA);
        // Reverse the switch terminal: the line remains energized and charges from A.
        d = d with { Switches = [d.Switches[0] with { BusId = "B", NormallyClosed = false }] };
        world = new GridWorld(d, 1, TimeSpan.Zero);
        Assert.InRange(world.Snapshot().Branch("L")!.CurrentA, 1, 5);
        Assert.Equal(0, world.Snapshot().Branch("L")!.ReactivePowerToMvar);
        Assert.False(world.Snapshot().Bus("B")!.Energized);
        return Task.CompletedTask;
    }

    [Fact]
    public Task IsolatedBranchIsDeadEvenWhenBothExternalBusesAreLive()
    {
        var d = GridEngineNetworks.TwoBus();
        d = d with
        {
            Loads = [],
            Sources = [.. d.Sources, d.Sources[0] with { Id = "S2", BusId = "B" }],
            Switches = [d.Switches[0] with { NormallyClosed = false },
                d.Switches[0] with { Id = "Q2", BusId = "B", NormallyClosed = false }]
        };
        var world = new GridWorld(d, 1, TimeSpan.Zero);
        world.SetFault("L", GridEquipmentFault.ThreePhaseShortCircuit, "instructor");
        Assert.All(world.Snapshot().Buses, b => Assert.True(b.Energized));
        Assert.False(world.Snapshot().Branch("L")!.Energized);
        Assert.Equal(0, world.Snapshot().Branch("L")!.CurrentA);
        Assert.True(world.Snapshot().Converged);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(1)]
    [InlineData(60)]
    public Task InverseRelayCrossingDoesNotDependOnOuterStepOrFaultFlag(double step)
    {
        var d = GridEngineNetworks.TwoBus();
        var initial = new GridWorld(d, 1, TimeSpan.Zero);
        var current = initial.Snapshot().Branch("L")!.CurrentA;
        d = d with
        {
            Protections = [new("P", "Relay", "Q", GridProtectionFunction.Overcurrent51,
            GridCurveType.IecStandardInverse, current / 2, 0.15, true)]
        };
        var world = new GridWorld(d, 1, TimeSpan.Zero);
        var expected = GridProtectionCurves.OperateSeconds(GridCurveType.IecStandardInverse, 2, 0.15);
        for (var i = 0; i < Math.Ceiling(3 / step); i++) world.Step(step);
        Assert.False(world.Switches["Q"].Closed);
        Assert.InRange(Math.Abs(world.Relays["P"].LastTripAt!.Value.TotalSeconds - expected), 0, 1e-6);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(0.07)]
    [InlineData(1)]
    [InlineData(60)]
    public Task VaryingProfileRelayTimingUsesTheSameInternalSamples(double outerStep)
    {
        var d = GridEngineNetworks.TwoBus();
        d = d with { Loads = [d.Loads[0] with { Profile = GridLoadProfile.Residential }] };
        var initial = new GridWorld(d, 1, TimeSpan.FromHours(18));
        d = d with
        {
            Protections = [new("P", "Relay", "Q", GridProtectionFunction.Overcurrent51,
            GridCurveType.IecStandardInverse, initial.Snapshot().Branch("L")!.CurrentA / 2, 0.15, true)]
        };
        var reference = new GridWorld(d, 1, TimeSpan.FromHours(18));
        var actual = new GridWorld(d, 1, TimeSpan.FromHours(18));
        for (var i = 0; i < 300; i++) reference.Step(0.01);
        for (var i = 0; i < Math.Ceiling(3 / outerStep); i++) actual.Step(outerStep);
        Assert.NotNull(actual.Relays["P"].LastTripAt);
        Assert.InRange(Math.Abs((actual.Relays["P"].LastTripAt!.Value - reference.Relays["P"].LastTripAt!.Value).TotalSeconds), 0, 2e-6);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(0.07)]
    [InlineData(1)]
    [InlineData(60)]
    public Task OneRelayOpeningCannotRetroactivelyChangeAnotherRelaysElapsedCurrent(double step)
    {
        var d = DefaultGridDefinition.Create() with
        {
            Protections =
        [
            new("P1", "Fast earth", "QF1", GridProtectionFunction.EarthFault51N, GridCurveType.DefiniteTime, 50, 0.2, true),
            new("P2", "Inverse earth", "QF2", GridProtectionFunction.EarthFault51N, GridCurveType.IecStandardInverse, 50, 0.2, true)
        ]
        };
        var reference = new GridWorld(d, 1, TimeSpan.FromHours(8));
        var actual = new GridWorld(d, 1, TimeSpan.FromHours(8));
        foreach (var world in new[] { reference, actual })
        {
            world.SetFault("F1-2", GridEquipmentFault.EarthFault, "instructor");
            world.SetFault("F2-2", GridEquipmentFault.EarthFault, "instructor");
        }
        for (var i = 0; i < 200; i++) reference.Step(0.01);
        for (var i = 0; i < Math.Ceiling(2 / step); i++) actual.Step(step);
        Assert.NotNull(reference.Relays["P2"].LastTripAt);
        Assert.NotNull(actual.Relays["P2"].LastTripAt);
        Assert.InRange(Math.Abs((actual.Relays["P2"].LastTripAt!.Value - reference.Relays["P2"].LastTripAt!.Value).TotalSeconds), 0, 2e-6);
        return Task.CompletedTask;
    }

    [Fact]
    public Task AvrDelayBeginsAfterFaultClearanceRatherThanBeforeTheOpeningEvent()
    {
        var d = DefaultGridDefinition.Create();
        d = d with { Transformers = d.Transformers.Select(t => t.Id == "TR1" ? t with { InitialTap = 0 } : t).ToArray() };
        var coarse = new GridWorld(d, 1, TimeSpan.FromHours(8));
        var fine = new GridWorld(d, 1, TimeSpan.FromHours(8));
        foreach (var world in new[] { coarse, fine }) world.SetFault("F1-2", GridEquipmentFault.ThreePhaseShortCircuit, "instructor");
        coarse.Step(60);
        for (var i = 0; i < 6000; i++) fine.Step(0.01);
        Assert.Equal(1, coarse.Taps["TR1"]);
        Assert.Equal(fine.Taps["TR1"], coarse.Taps["TR1"]);
        return Task.CompletedTask;
    }

    [Fact]
    public Task OpeningAndReclosingImmediatelyResetsRelayProgress()
    {
        var d = GridEngineNetworks.TwoBus() with
        {
            Protections = [new("P", "Relay", "Q", GridProtectionFunction.Overcurrent51,
            GridCurveType.DefiniteTime, 1, 1, true)]
        };
        var world = new GridWorld(d, 1, TimeSpan.Zero);
        world.Step(0.9);
        world.OperateSwitch("Q", false, "operator", GridChangeCause.Operator);
        world.OperateSwitch("Q", true, "operator", GridChangeCause.Operator);
        world.Step(0.2);
        Assert.True(world.Switches["Q"].Closed);
        world.Step(0.8);
        Assert.False(world.Switches["Q"].Closed);
        Assert.Equal(TimeSpan.FromSeconds(1.9), world.Relays["P"].LastTripAt);
        return Task.CompletedTask;
    }

    [Fact]
    public Task SupervisedZeroVoltageTrips27ButDeadBusDoesNot()
    {
        var d = DefaultGridDefinition.Create();
        d = d with { Protections = d.Protections.Where(p => p.Function == GridProtectionFunction.Undervoltage27).ToArray() };
        var world = new GridWorld(d, 1, TimeSpan.Zero);
        world.SetFault("MV", GridEquipmentFault.ThreePhaseShortCircuit, "instructor");
        Assert.InRange(world.Snapshot().Bus("MV")!.VoltagePu, 0, 1e-9);
        world.Step(5);
        Assert.False(world.Switches["Q0"].Closed);
        Assert.True(world.Relays["MV-27"].Tripped);
        world = new GridWorld(d, 1, TimeSpan.Zero);
        world.OperateSwitch("Q0", false, "operator", GridChangeCause.Operator);
        world.Step(60);
        Assert.False(world.Relays["MV-27"].PickedUp);
        Assert.False(world.Relays["MV-27"].Tripped);
        Assert.DoesNotContain(world.Snapshot().Alarms, a => a.Active && a.Type == GridAlarmType.Undervoltage);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task InsufficientBreakingCapacityFailsManualAndProtectionOpeningWithBackup(bool automatic)
    {
        var d = DefaultGridDefinition.Create();
        d = d with { Switches = d.Switches.Select(s => s.Id == "QF1" ? s with { BreakingCapacityKa = 1 } : s).ToArray() };
        var world = new GridWorld(d, 1, TimeSpan.Zero);
        world.SetFault("F1-2", GridEquipmentFault.ThreePhaseShortCircuit, "instructor");
        if (automatic) world.Step(0.1);
        else Assert.False(world.OperateSwitch("QF1", false, "operator", GridChangeCause.Operator).Accepted);
        Assert.True(world.Switches["QF1"].Closed);
        Assert.False(world.Relays["QF1-50"].Tripped);
        Assert.Contains(world.Snapshot().Alarms, a => a.Type == GridAlarmType.BreakerFailure && a.Active);
        world.Step(1.2);
        Assert.False(world.Switches["Q0"].Closed);
        return Task.CompletedTask;
    }

    [Fact]
    public Task UnratedLoadBreakSwitchCannotInterruptBackfedFault()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 1, TimeSpan.Zero);
        world.OperateSwitch("TIE", true, "operator", GridChangeCause.Operator);
        world.SetFault("F2-1", GridEquipmentFault.ThreePhaseShortCircuit, "instructor");
        world.OperateSwitch("QF2", false, "operator", GridChangeCause.Operator);
        Assert.False(world.OperateSwitch("TIE", false, "operator", GridChangeCause.Operator).Accepted);
        Assert.True(world.Switches["TIE"].Closed);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("default", "F3-3-LV", "TS33-LV-Q", "QF3")]
    [InlineData("rural-overhead-20kv", "N10-LV", "N10-TR-LV-Q", "FC-QF")]
    [InlineData("urban-cable-20kv", "SS11-LV", "SS11-TR-LV-Q", "F1-QF")]
    public Task LocalTransformerProtectionClearsLvFaultBeforeFeeder(string template, string bus, string local, string feeder)
    {
        var world = new GridWorld(GridTemplateCatalog.Find(template)!.Factory(), 1, TimeSpan.FromHours(19));
        Assert.True(world.SetFault(bus, GridEquipmentFault.ThreePhaseShortCircuit, "instructor").Accepted);
        world.Step(0.2);
        Assert.False(world.Switches[local].Closed);
        Assert.True(world.Switches[feeder].Closed);
        Assert.True(world.Snapshot().Converged);
        return Task.CompletedTask;
    }

    [Fact]
    public Task FaultPowerIsBalancedPrefaultEstimateWithUncertainQualityAndSerializableSnapshot()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 42, TimeSpan.FromHours(19));
        world.SetFault("MV", GridEquipmentFault.ThreePhaseShortCircuit, "instructor");
        var s = world.Snapshot();
        Assert.False(s.Converged);
        Assert.DoesNotContain(s.Alarms, a => a.Type == GridAlarmType.SolverDivergence && a.Active);
        Assert.All(s.Measurements.Where(m => m.Kind is GridMeasurementKind.ActivePower or GridMeasurementKind.ReactivePower),
            m => Assert.Equal(MeasurementQuality.Uncertain, m.Quality));
        Assert.InRange(Math.Abs(s.Totals.SourceImportMw + s.Totals.GenerationMw - s.Totals.LoadMw - s.Totals.LossesMw), 0, 0.001);
        Assert.NotEmpty(JsonSerializer.Serialize(s));
        return Task.CompletedTask;
    }

    [Fact]
    public Task DivergedFlowFreezesPowerMarksQualityAndSuspendsRelaysAndAvr()
    {
        var d = DefaultGridDefinition.Create();
        d = d with
        {
            Capacitors = [d.Capacitors[0] with { RatedPowerMvar = 1e9 }],
            Transformers = d.Transformers.Select(t => t.Id == "TR1" ? t with { InitialTap = 0 } : t).ToArray()
        };
        var world = new GridWorld(d, 42, TimeSpan.FromHours(8));
        var initial = world.Snapshot();
        world.SetProtection("Q0-51", 1, 0.1, true, "instructor");
        world.ApplyCommand(new(GridCommandType.SetCapacitorSteps, "CAP1", 4), "operator", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30));
        world.Step(60);
        var stale = world.Snapshot();
        Assert.False(stale.Converged);
        Assert.Equal(initial.Totals, stale.Totals);
        Assert.Equal(0, world.Taps["TR1"]);
        Assert.True(world.Switches["Q0"].Closed);
        Assert.False(world.OperateSwitch("Q0", false, "operator", GridChangeCause.Operator).Accepted);
        Assert.All(stale.Measurements.Where(m => m.Kind != GridMeasurementKind.SwitchState && m.Kind != GridMeasurementKind.TapPosition),
            m => Assert.Equal(MeasurementQuality.Uncertain, m.Quality));
        return Task.CompletedTask;
    }

    [Fact]
    public Task ExcitationConsumesIronWattsAndMagnetisingVars()
    {
        var d = DefaultGridDefinition.Create() with { Loads = [], Generators = [], Lines = [], Switches = [], Protections = [] };
        d = d with { Transformers = [d.Transformers[0]] };
        var world = new GridWorld(d, 1, TimeSpan.Zero);
        var branch = world.Snapshot().Branch("TR1")!;
        Assert.InRange(branch.ActivePowerFromMw, 0.020, 0.024);
        Assert.InRange(branch.ReactivePowerFromMvar, 0.055, 0.065);
        return Task.CompletedTask;
    }

    [Fact]
    public Task AvrReversalAndManualModeRequireAFreshContinuousDelay()
    {
        var d = DefaultGridDefinition.Create();
        d = d with
        {
            Transformers = d.Transformers.Select(t => t.Id == "TR1" ? t with { InitialTap = 0 } : t).ToArray(),
            Capacitors = [d.Capacitors[0] with { RatedPowerMvar = 15 }]
        };
        var world = new GridWorld(d, 1, TimeSpan.FromHours(8));
        world.Step(29);
        world.ApplyCommand(new(GridCommandType.SetCapacitorSteps, "CAP1", 4), "operator", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30));
        Assert.True(world.Snapshot().Bus("MV")!.VoltageKv > 20.654);
        world.Step(1);
        Assert.Equal(0, world.Taps["TR1"]);
        world.Step(29);
        Assert.Equal(-1, world.Taps["TR1"]);
        world = new GridWorld(d, 1, TimeSpan.FromHours(8));
        world.Step(29);
        world.ApplyCommand(new(GridCommandType.SetTapManual, "TR1"), "operator", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30));
        world.ApplyCommand(new(GridCommandType.SetTapAutomatic, "TR1"), "operator", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(30));
        world.Step(1);
        Assert.Equal(0, world.Taps["TR1"]);
        world.Step(29);
        Assert.Equal(1, world.Taps["TR1"]);
        return Task.CompletedTask;
    }

    [Fact]
    public Task AvrProcessesMultipleElapsedIntervalsInOneOuterStep()
    {
        var d = DefaultGridDefinition.Create();
        d = d with { Transformers = d.Transformers.Select(t => t.Id == "TR1" ? t with { InitialTap = -4 } : t).ToArray() };
        var coarse = new GridWorld(d, 1, TimeSpan.FromHours(8));
        var fine = new GridWorld(d, 1, TimeSpan.FromHours(8));
        coarse.Step(90);
        for (var i = 0; i < 90; i++) fine.Step(1);
        Assert.Equal(-1, coarse.Taps["TR1"]);
        Assert.Equal(fine.Taps["TR1"], coarse.Taps["TR1"]);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ProfilesUseEndTimeAndIncludeSeasonWeatherWeekendAndCommonDemand()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 1, TimeSpan.FromHours(18));
        world.Step(3600);
        var load = world.Definition.Loads.First(l => l.Id == "L11");
        Assert.Equal(load.PeakActivePowerMw * GridProfiles.Load(load.Profile, TimeSpan.FromHours(19)) * GridProfiles.DemandVariation(TimeSpan.FromHours(19)), world.Demand[load.Id], 12);
        Assert.Equal(0, GridProfiles.Sun(TimeSpan.FromHours(19)));
        Assert.InRange(GridProfiles.Sun(TimeSpan.FromHours(12)), 0.5, 0.95);
        Assert.NotEqual(GridProfiles.Sun(TimeSpan.FromHours(12)), GridProfiles.Sun(TimeSpan.FromDays(90) + TimeSpan.FromHours(12)));
        Assert.True(GridProfiles.Load(GridLoadProfile.Commercial, TimeSpan.FromDays(5) + TimeSpan.FromHours(12)) < GridProfiles.Load(GridLoadProfile.Commercial, TimeSpan.FromHours(12)));
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task DenseSolverLimitsRejectOversizedDefinitionsBeforeBuildingMatrices(bool electrical)
    {
        var d = DefaultGridDefinition.Create();
        d = electrical ? d with { Buses = Enumerable.Range(0, 257).Select(i => d.Buses[0] with { Id = $"B{i}" }).ToArray() }
            : d with { Loads = Enumerable.Range(0, 2001).Select(i => d.Loads[0] with { Id = $"L{i}" }).ToArray() };
        var issue = Assert.Single(GridDefinitionValidator.Validate(d));
        Assert.Contains(electrical ? "solver-node budget" : "element limit", issue);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(GridScenarioTargetKind.MostLoadedFeeder)]
    [InlineData(GridScenarioTargetKind.FeederRtu)]
    [InlineData(GridScenarioTargetKind.FeederCurrentMeasurements)]
    public Task LineLessScenarioTargetsFailCleanly(GridScenarioTargetKind kind)
    {
        var d = GridEngineNetworks.TwoBus() with { Lines = [], Switches = [], Loads = [], Rtus = [] };
        var view = new GridEngine(d, new(), TimeProvider.System);
        var result = GridScenarioTargetResolver.Resolve(view, [new(kind)]);
        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
        return Task.CompletedTask;
    }
}
