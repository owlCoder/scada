using Application.Events;
using Application.Grid;
using Application.Realtime;
using Domain.Grid.Snapshots;
using Microsoft.Extensions.Logging.Abstractions;
using Simulation.HostedServices;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Grid;

public sealed class GridEngineRealtimeAndHostingTests
{
    [Fact]
    public async Task CoordinatorPublishesRedactedSnapshotToGridTopicsAndEventsToTheAuditLog()
    {
        var realtime = new GridEngineRealtimeCollector();
        var rig = new GridEngineTestRig(realtime: realtime);
        await rig.Training.InjectFaultAsync("F1-2", GridEquipmentFault.ThreePhaseShortCircuit, GridEngineTestRig.Administrator, CancellationToken.None);
        Assert.NotNull(await rig.Coordinator.StepAsync(false, CancellationToken.None));
        var messages = realtime.Snapshot();
        Assert.Contains(messages, m => m.Topic == RealtimeTopics.Grid);
        Assert.Contains(messages, m => m.Topic == RealtimeTopics.GridAlarms);
        Assert.Contains(messages, m => m.Topic == RealtimeTopics.GridStatus);
        var grid = Assert.IsType<GridSnapshot>(messages.Last(m => m.Topic == RealtimeTopics.Grid).Payload);
        Assert.All(grid.Branches, b => Assert.Equal(GridEquipmentFault.None, b.Fault));
        Assert.All(grid.Measurements, m => { Assert.False(m.IsSpoofed); Assert.Equal(m.ReportedValue, m.TrueValue); });
        var alarms = Assert.IsAssignableFrom<IReadOnlyList<GridAlarm>>(messages.Last(m => m.Topic == RealtimeTopics.GridAlarms).Payload);
        Assert.NotNull(alarms);
        var status = Assert.IsType<GridStatus>(messages.Last(m => m.Topic == RealtimeTopics.GridStatus).Payload);
        Assert.Equal("Central European distribution grid", status.DefinitionName);
        Assert.Contains(rig.Events.Items, e => e.Category == EventCategories.Scenario && e.Message.Contains("Fault", StringComparison.Ordinal));
        Assert.Contains(rig.Events.Items, e => e.Source == "grid:training" && e.Category == EventCategories.Audit);
    }

    [Fact]
    public async Task GridSimulationServiceLoadsTheActiveDefinitionVersionOnStartup()
    {
        var rig = new GridEngineTestRig();
        var edited = rig.Engine.Definition with { Name = "Hosted grid v2" };
        var saved = await rig.Store.SaveAsync(edited, "instructor", "hosting test", CancellationToken.None);
        Assert.True(await rig.Store.SetActiveAsync(saved.Version, CancellationToken.None));
        var service = new GridSimulationService(rig.Coordinator, rig.Engine, rig.Store, rig.Options, NullLogger<GridSimulationService>.Instance);
        await service.StartAsync(CancellationToken.None);
        try
        {
            Assert.Equal("Hosted grid v2", rig.Engine.Definition.Name);
            Assert.Equal(saved.Version, rig.Engine.Status.DefinitionVersion);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DisabledGridServiceLoadsButNeverSteps()
    {
        var options = new GridOptions { Enabled = false };
        var rig = new GridEngineTestRig(options);
        var service = new GridSimulationService(rig.Coordinator, rig.Engine, rig.Store, options, NullLogger<GridSimulationService>.Instance);
        await service.StartAsync(CancellationToken.None);
        var before = rig.Engine.Status.StepCount;
        await Task.Delay(150);
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(before, rig.Engine.Status.StepCount);
        Assert.Equal("Central European distribution grid", rig.Engine.Definition.Name);
    }
}
