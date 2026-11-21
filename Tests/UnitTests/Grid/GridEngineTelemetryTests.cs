using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Network;
using Domain.Grid.Snapshots;
using Domain.Process.Devices;
using Xunit;

namespace UnitTests.Grid;

public sealed class GridEngineTelemetryTests
{
    [Fact]
    public Task SeededNoiseIsBoundedAndRepeatable()
    {
        var a = new GridWorld(DefaultGridDefinition.Create(), 7, TimeSpan.FromHours(8));
        var b = new GridWorld(DefaultGridDefinition.Create(), 7, TimeSpan.FromHours(8));
        var first = a.Step(1); var second = b.Step(1);
        Assert.Equal(first.Measurements, second.Measurements);
        Assert.All(first.Measurements.Where(m => m.TrueValue != 0), m => Assert.InRange(Math.Abs(m.ReportedValue / m.TrueValue - 1), 0, 0.002000001));
        Assert.Contains(first.Measurements, m => m.ReportedValue != m.TrueValue);
        Assert.InRange(first.FrequencyHz, 49.995, 50.005);
        return Task.CompletedTask;
    }

    [Fact]
    public Task SpoofOverridesReportedValueAndOfflineFreezesLastReportedTelemetry()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 7, TimeSpan.FromHours(8));
        var id = "RTU2:F1-1-MV:Voltage";
        world.SetSpoof(id, 19.8, "instructor");
        var spoof = world.Snapshot().Measurements.Single(m => m.Id == id);
        Assert.True(spoof.IsSpoofed);
        Assert.Equal(19.8, spoof.ReportedValue);
        Assert.NotEqual(spoof.TrueValue, spoof.ReportedValue);
        world.SetRtuOnline("RTU2", false, "instructor");
        world.OperateSwitch("QF1", false, "instructor", GridChangeCause.Unattributed);
        world.Step(1);
        var offline = world.Snapshot().Measurements.Single(m => m.Id == id);
        Assert.Equal(MeasurementQuality.Bad, offline.Quality);
        Assert.Equal(19.8, offline.ReportedValue);
        Assert.Equal(0, offline.TrueValue);
        world.SetSpoof(id, null, "instructor");
        world.SetRtuOnline("RTU2", true, "instructor");
        Assert.Equal(0, world.Snapshot().Measurements.Single(m => m.Id == id).ReportedValue);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RedactorHidesFaultsTrueValuesSpoofingSettingsAndUnattributedActor()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 7, TimeSpan.FromHours(8));
        world.SetFault("F1-2", GridEquipmentFault.ThreePhaseShortCircuit, "instructor");
        world.SetProtection("QF1-50", 1, 0.05, true, "instructor");
        world.Step(0.3);
        world.OperateSwitch("QF2", false, "attacker", GridChangeCause.Unattributed);
        world.SetSpoof("RTU3:QF2:SwitchState", 1, "instructor");
        world.SetSpoof("RTU2:F1-1-MV:Voltage", 20, "instructor");
        var view = GridViewRedactor.ForOperators(world.Snapshot(), world.Definition);
        Assert.All(view.Branches, x => Assert.Equal(GridEquipmentFault.None, x.Fault));
        Assert.All(view.Switches, x => Assert.Equal(GridEquipmentFault.None, x.Fault));
        Assert.All(view.Protections, x => Assert.False(x.SettingsChanged));
        Assert.All(view.Measurements, m => { Assert.Equal(m.ReportedValue, m.TrueValue); Assert.False(m.IsSpoofed); Assert.Null(m.SpoofedValue); });
        Assert.Equal(20, view.Bus("F1-1-MV")!.VoltageKv);
        Assert.Null(view.Switch("QF2")!.LastOperatedBy);
        Assert.True(view.Switch("QF2")!.Closed);
        Assert.False(view.Switch("QF1")!.Closed);
        Assert.Contains(view.Protections, p => p.Tripped);
        Assert.Equal(1500, view.Protections.Single(p => p.Id == "QF1-50").PickupValue);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RedactorComputesImportAndLossesFromReportedMeasurements()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 104, TimeSpan.FromHours(8));
        world.Step(1);
        var truth = world.Snapshot();
        var view = GridViewRedactor.ForOperators(truth, world.Definition);

        var tr1Reported = truth.Measurements.Single(m => m.ElementId == "TR1" && m.Kind == GridMeasurementKind.ActivePower).ReportedValue;
        Assert.Equal(tr1Reported, view.Totals.SourceImportMw, 9);
        Assert.InRange(view.Totals.SourceImportMw / truth.Totals.SourceImportMw, 0.99, 1.01);
        Assert.True(view.Totals.SourceImportMw > 0);
        var load = view.Totals.LoadMw;
        var generation = view.Totals.GenerationMw;
        Assert.Equal(Math.Max(0, view.Totals.SourceImportMw + generation - load), view.Totals.LossesMw, 9);
        // The reported balance differs from the true losses only by telemetry noise, and stays positive and small.
        Assert.InRange(view.Totals.LossesMw / truth.Totals.SourceImportMw, 0, 0.05);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RedactorTotalsFollowSpoofedSourcePowerAndShedLoad()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 104, TimeSpan.FromHours(8));
        world.Step(1);
        // Spoof the 110/20 kV transformer power to a low value: the operator's import and losses follow the spoof.
        world.SetSpoof("RTU1:TR1:ActivePower", 0.5, "attacker");
        var spoofed = GridViewRedactor.ForOperators(world.Snapshot(), world.Definition);
        Assert.Equal(0.5, spoofed.Totals.SourceImportMw, 9);
        Assert.Equal(Math.Max(0, 0.5 + spoofed.Totals.GenerationMw - spoofed.Totals.LoadMw), spoofed.Totals.LossesMw, 9);
        // The administrator still sees the true import.
        Assert.True(world.Snapshot().Totals.SourceImportMw > spoofed.Totals.SourceImportMw);
        world.SetSpoof("RTU1:TR1:ActivePower", null, "instructor");

        // Open the incomer: reported import collapses and every customer is unsupplied.
        world.OperateSwitch("Q0", false, "instructor", GridChangeCause.Operator);
        world.Step(1);
        var deenergized = GridViewRedactor.ForOperators(world.Snapshot(), world.Definition);
        Assert.True(deenergized.Totals.SourceImportMw < 0.01);
        Assert.Equal(0, deenergized.Totals.CustomersSupplied);
        Assert.Equal(0, deenergized.Totals.LossesMw);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RedactorComputesImportWithoutDefinitionFromSourceSideBuses()
    {
        var world = new GridWorld(DefaultGridDefinition.Create(), 104, TimeSpan.FromHours(8));
        world.Step(1);
        var withDefinition = GridViewRedactor.ForOperators(world.Snapshot(), world.Definition);
        var without = GridViewRedactor.ForOperators(world.Snapshot());
        Assert.Equal(withDefinition.Totals.SourceImportMw, without.Totals.SourceImportMw, 9);
        Assert.Equal(withDefinition.Totals.LossesMw, without.Totals.LossesMw, 9);
        return Task.CompletedTask;
    }
}
