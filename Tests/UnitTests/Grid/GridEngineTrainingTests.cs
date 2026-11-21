using Application.Events;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;
using Domain.Process.Devices;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Grid;

public sealed class GridEngineTrainingTests
{
    [Fact]
    public async Task EveryTrainingActionIsAuditedAndAffectsTheWorld()
    {
        var rig = new GridEngineTestRig();
        var admin = GridEngineTestRig.Administrator;
        var none = CancellationToken.None;

        Assert.True((await rig.Training.InjectFaultAsync("F1-2", GridEquipmentFault.ThreePhaseShortCircuit, admin, none)).Accepted);
        Assert.True((await rig.Coordinator.StepAsync(false, none))!.Snapshot.Branch("F1-2")!.Fault == GridEquipmentFault.ThreePhaseShortCircuit);
        Assert.True((await rig.Training.ClearFaultAsync("F1-2", admin, none)).Accepted);
        Assert.Equal(GridEquipmentFault.None, rig.Engine.Snapshot().Branch("F1-2")!.Fault);

        Assert.True((await rig.Training.SpoofMeasurementAsync("RTU2:F1-1-MV:Voltage", 19.5, admin, none)).Accepted);
        var spoofed = rig.Engine.Snapshot().Measurements.Single(m => m.Id == "RTU2:F1-1-MV:Voltage");
        Assert.True(spoofed.IsSpoofed);
        Assert.Equal(19.5, spoofed.ReportedValue);

        Assert.True((await rig.Training.ChangeProtectionAsync("QF1-50", 100000, 0.08, true, admin, none)).Accepted);
        Assert.True(rig.Engine.Snapshot().Protections.Single(p => p.Id == "QF1-50").SettingsChanged);
        Assert.Equal(100000, rig.Engine.Snapshot().Protections.Single(p => p.Id == "QF1-50").PickupValue);

        Assert.True((await rig.Training.ForceSwitchAsync("QF3", false, admin, none)).Accepted);
        Assert.False(rig.Engine.Snapshot().Switch("QF3")!.Closed);
        Assert.Equal(GridChangeCause.Unattributed, rig.Engine.Snapshot().Switch("QF3")!.LastCause);

        Assert.True((await rig.Training.ForceTapAsync("TR1", 5, admin, none)).Accepted);
        Assert.Equal(5, rig.Engine.Read(w => w.Taps["TR1"]));

        Assert.True((await rig.Training.SetRtuOnlineAsync("RTU4", false, admin, none)).Accepted);
        Assert.False(rig.Engine.Read(w => w.RtuOnline["RTU4"]));

        Assert.True((await rig.Training.RestoreTrainingDefaultsAsync(admin, none)).Accepted);
        var restored = rig.Engine.Snapshot();
        Assert.All(restored.Branches, b => Assert.Equal(GridEquipmentFault.None, b.Fault));
        Assert.All(restored.Measurements, m => Assert.False(m.IsSpoofed));
        Assert.False(restored.Protections.Single(p => p.Id == "QF1-50").SettingsChanged);
        Assert.All(restored.Measurements.Where(m => m.RtuId == "RTU4"), m => Assert.Equal(MeasurementQuality.Good, m.Quality));

        var trainingEvents = rig.Events.Items.Where(e => e.Source == "grid:training").ToArray();
        Assert.Equal(8, trainingEvents.Count(e => e.Category == EventCategories.Audit));
        Assert.Contains(rig.Events.Items, e => e.Category == EventCategories.Scenario && e.Message.Contains("overridden", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TrainingRejectsNonAdministratorsWithAnAuthorizationEvent()
    {
        var rig = new GridEngineTestRig();
        var result = await rig.Training.InjectFaultAsync("F1-2", GridEquipmentFault.EarthFault, GridEngineTestRig.Operator, CancellationToken.None);
        Assert.Equal(GridCommandRejection.NotAuthorized, result.Reason);
        Assert.False(rig.Engine.Snapshot().Branch("F1-2")!.Fault == GridEquipmentFault.EarthFault);
        Assert.Contains(rig.Events.Items, e => e.Category == EventCategories.AuthorizationDenied && e.Actor == "ana");
    }
}
