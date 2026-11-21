using Application.Events;
using Application.Identity;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;
using Security.Commands;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Grid;

public sealed class GridEngineControlTests
{
    [Fact]
    public async Task SelectExecuteCancelWrongActorAndExpiryUseWallClock()
    {
        var rig = new GridEngineTestRig();
        Assert.Equal(GridCommandRejection.NotSelected, (await rig.Command(GridCommandType.Execute, "QF1")).Reason);
        Assert.True((await rig.Command(GridCommandType.SelectOpen, "QF1")).Accepted);
        Assert.True(rig.Engine.Snapshot().Switch("QF1")!.Selected);
        Assert.Equal(GridCommandRejection.NotSelected, (await rig.Command(GridCommandType.Execute, "QF1", actor: new("boris", AccessRole.Operator))).Reason);
        Assert.True((await rig.Command(GridCommandType.Cancel, "QF1")).Accepted);
        Assert.Equal(GridCommandRejection.NotSelected, (await rig.Command(GridCommandType.Execute, "QF1")).Reason);
        await rig.Command(GridCommandType.SelectOpen, "QF1");
        rig.Time.Advance(TimeSpan.FromSeconds(31));
        Assert.False(rig.Engine.Snapshot().Switch("QF1")!.Selected);
        Assert.Equal(GridCommandRejection.SelectionExpired, (await rig.Command(GridCommandType.Execute, "QF1")).Reason);
        await rig.Command(GridCommandType.SelectOpen, "QF1");
        Assert.True((await rig.Command(GridCommandType.Execute, "QF1")).Accepted);
        Assert.False(rig.Engine.Snapshot().Switch("QF1")!.Closed);
        Assert.Equal(9, rig.Events.Items.Count(e => e.Source == "grid:test" && e.Category is EventCategories.Audit or EventCategories.CommandRejected));
    }

    [Fact]
    public async Task SelectSurvivesSimulatedTimeAndConfigurableTimeoutIsHonored()
    {
        var rig = new GridEngineTestRig(new() { SelectTimeoutSeconds = 2 });
        await rig.Command(GridCommandType.SelectOpen, "QF1");
        for (var i = 0; i < 3; i++) rig.Engine.Step();
        Assert.True((await rig.Command(GridCommandType.Execute, "QF1")).Accepted);
        await rig.Command(GridCommandType.SelectClose, "QF1");
        rig.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(GridCommandRejection.SelectionExpired, (await rig.Command(GridCommandType.Execute, "QF1")).Reason);
    }

    [Fact]
    public async Task LoadedDisconnectorInterlocksAndOfflineRtuRejectsControl()
    {
        var rig = new GridEngineTestRig();
        await rig.Command(GridCommandType.SelectOpen, "DS0");
        Assert.Equal(GridCommandRejection.Interlocked, (await rig.Command(GridCommandType.Execute, "DS0")).Reason);
        Assert.True(rig.Engine.Snapshot().Switch("DS0")!.Closed);
        await rig.Training.SetRtuOnlineAsync("RTU2", false, GridEngineTestRig.Administrator, CancellationToken.None);
        Assert.Equal(GridCommandRejection.CommunicationLoss, (await rig.Command(GridCommandType.SelectOpen, "QF1")).Reason);
    }

    [Fact]
    public async Task RolesReplayRateLimitAndTrainingRejectionsAreAudited()
    {
        var rig = new GridEngineTestRig(rate: new CommandRateOptions { Limit = 3 });
        Assert.Equal(GridCommandRejection.NotAuthorized, (await rig.Command(GridCommandType.SelectOpen, "QF1", actor: new("viewer", AccessRole.Viewer))).Reason);
        Assert.Equal(GridCommandRejection.NotAuthorized,
            (await rig.Training.InjectFaultAsync("F1-1", GridEquipmentFault.EarthFault, GridEngineTestRig.Operator, CancellationToken.None)).Reason);
        Assert.True((await rig.Command(GridCommandType.SelectOpen, "QF1", sequence: 2)).Accepted);
        Assert.Equal(GridCommandRejection.Replay, (await rig.Command(GridCommandType.Execute, "QF1", sequence: 2)).Reason);
        Assert.True((await rig.Command(GridCommandType.Execute, "QF1", sequence: 3)).Accepted);
        Assert.Equal(GridCommandRejection.RateLimited, (await rig.Command(GridCommandType.Cancel, "QF1")).Reason);
        Assert.Equal(2, rig.Events.Items.Count(e => e.Category == EventCategories.AuthorizationDenied));
        Assert.Contains(rig.Events.Items, e => e.Source == "grid:test" && e.Message.Contains("Replay", StringComparison.Ordinal));
        Assert.Contains(rig.Events.Items, e => e.Source == "grid:test" && e.Message.Contains("RateLimited", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RuntimeRolesPauseResetAndVersionActivationWork()
    {
        var rig = new GridEngineTestRig();
        var none = CancellationToken.None;
        var viewer = new Actor("viewer", AccessRole.Viewer);
        Assert.False((await rig.Coordinator.SetPausedAsync(true, viewer, none)).Accepted);
        Assert.True((await rig.Coordinator.SetPausedAsync(true, GridEngineTestRig.Operator, none)).Accepted);
        Assert.Null(await rig.Coordinator.StepAsync(false, none));
        Assert.NotNull(await rig.Coordinator.StepAsync(true, none));
        Assert.False((await rig.Coordinator.ResetAsync(GridEngineTestRig.Operator, none)).Accepted);
        Assert.True((await rig.Coordinator.ResetAsync(GridEngineTestRig.Administrator, none)).Accepted);
        Assert.Equal(0, rig.Engine.Status.StepCount);
        var saved = await rig.Coordinator.SaveAsync(rig.Engine.Definition with { Name = "Edited grid" }, "exercise", GridEngineTestRig.Administrator, none);
        Assert.True(saved.Saved);
        Assert.True((await rig.Coordinator.ActivateVersionAsync(saved.Version!.Value, GridEngineTestRig.Administrator, none)).Accepted);
        Assert.Equal("Edited grid", rig.Engine.Definition.Name);
        Assert.Equal(2, rig.Engine.Status.DefinitionVersion);
        Assert.True((await rig.Store.GetAsync(2, none))!.Active);
    }
}
