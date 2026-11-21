using Application.Events;
using Application.Grid;
using Domain.Grid.Commands;
using UnitTests.Grid.Support;
using Xunit;

namespace UnitTests.Grid;

/// <summary>Tag enforcement in <see cref="Security.Grid.Control.GridControlService"/>: blocking kinds on the switch
/// itself, on the switch's element, and on tap/capacitor/load targets; Information never blocks; training actions
/// (ForceSwitch) deliberately ignore tags.</summary>
public sealed class GridControlTagBlockingTests
{
    public static TheoryData<GridTagKind> BlockingKinds => [GridTagKind.DoNotOperate, GridTagKind.Earthed, GridTagKind.WorkInProgress];

    [Theory]
    [MemberData(nameof(BlockingKinds))]
    public async Task TaggedSwitchRejectsSelectAndExecuteWithInterlockedNamingTheTag(GridTagKind kind)
    {
        var rig = new DispatcherGridRig();
        var placed = await rig.Tagging.PlaceTagAsync("QF1", kind, "hold for maintenance", DispatcherGridRig.Operator, CancellationToken.None);
        Assert.True(placed.Accepted);
        var tag = Assert.Single(rig.Tagging.Tags);

        foreach (var type in new[] { GridCommandType.SelectOpen, GridCommandType.SelectClose, GridCommandType.Execute })
        {
            var result = await rig.Command(type, "QF1");
            Assert.False(result.Accepted, $"{type} should be rejected");
            Assert.Equal(GridCommandRejection.Interlocked, result.Reason);
            Assert.Contains(tag.Id, result.Message, StringComparison.Ordinal);
            Assert.Contains("QF1", result.Message, StringComparison.Ordinal);
            Assert.Contains(kind.ToString(), result.Message, StringComparison.Ordinal);
        }
        // The rejections are audited like every command rejection.
        Assert.Contains(rig.Events.Items, e => e.Category == EventCategories.CommandRejected
            && e.Message.Contains(tag.Id, StringComparison.Ordinal));
        // The switch was never selected or operated.
        Assert.False(rig.Engine.Snapshot().Switch("QF1")!.Selected);
    }

    [Fact]
    public async Task TagOnTheSwitchesElementBlocksTheSwitch()
    {
        var rig = new DispatcherGridRig();
        Assert.True((await rig.Tagging.PlaceTagAsync("F1-1", GridTagKind.DoNotOperate, "line work", DispatcherGridRig.Operator, CancellationToken.None)).Accepted);
        var result = await rig.Command(GridCommandType.SelectOpen, "QF1");
        Assert.Equal(GridCommandRejection.Interlocked, result.Reason);
        Assert.Contains("F1-1", result.Message, StringComparison.Ordinal);
        // An unrelated switch on another element still works.
        Assert.True((await rig.Command(GridCommandType.SelectOpen, "QF2")).Accepted);
    }

    [Fact]
    public async Task TagOnTransformerBlocksItsSwitchesAndTapCommands()
    {
        var rig = new DispatcherGridRig();
        Assert.True((await rig.Tagging.PlaceTagAsync("TR1", GridTagKind.WorkInProgress, "transformer out", DispatcherGridRig.Operator, CancellationToken.None)).Accepted);
        Assert.Equal(GridCommandRejection.Interlocked, (await rig.Command(GridCommandType.SelectOpen, "Q0")).Reason);
        Assert.Equal(GridCommandRejection.Interlocked, (await rig.Command(GridCommandType.SelectOpen, "DS0")).Reason);
        Assert.Equal(GridCommandRejection.Interlocked, (await rig.Command(GridCommandType.TapRaise, "TR1")).Reason);
        Assert.Equal(GridCommandRejection.Interlocked, (await rig.Command(GridCommandType.TapLower, "TR1")).Reason);
        Assert.Equal(GridCommandRejection.Interlocked, (await rig.Command(GridCommandType.SetTapManual, "TR1")).Reason);
        Assert.Equal(GridCommandRejection.Interlocked, (await rig.Command(GridCommandType.SetTapAutomatic, "TR1")).Reason);
    }

    [Fact]
    public async Task TagOnLoadAndCapacitorBlocksLoadAndCapacitorCommands()
    {
        var rig = new DispatcherGridRig();
        Assert.True((await rig.Tagging.PlaceTagAsync("L11", GridTagKind.Earthed, "lv work", DispatcherGridRig.Operator, CancellationToken.None)).Accepted);
        Assert.True((await rig.Tagging.PlaceTagAsync("CAP1", GridTagKind.DoNotOperate, "bank test", DispatcherGridRig.Operator, CancellationToken.None)).Accepted);
        Assert.Equal(GridCommandRejection.Interlocked, (await rig.Command(GridCommandType.ShedLoad, "L11")).Reason);
        Assert.Equal(GridCommandRejection.Interlocked, (await rig.Command(GridCommandType.RestoreLoad, "L11")).Reason);
        Assert.Equal(GridCommandRejection.Interlocked, (await rig.Command(GridCommandType.SetCapacitorSteps, "CAP1", 2)).Reason);
        // Untagged targets still work.
        Assert.True((await rig.Command(GridCommandType.ShedLoad, "L12")).Accepted);
        Assert.True((await rig.Command(GridCommandType.SelectOpen, "QF2")).Accepted);
    }

    [Fact]
    public async Task InformationTagNeverBlocks()
    {
        var rig = new DispatcherGridRig();
        Assert.True((await rig.Tagging.PlaceTagAsync("QF1", GridTagKind.Information, "just a note", DispatcherGridRig.Operator, CancellationToken.None)).Accepted);
        Assert.True((await rig.Command(GridCommandType.SelectOpen, "QF1")).Accepted);
        Assert.True((await rig.Command(GridCommandType.Execute, "QF1")).Accepted);
        Assert.False(rig.Engine.Snapshot().Switch("QF1")!.Closed);
    }

    [Fact]
    public async Task CancelIsNotBlockedByATag()
    {
        var rig = new DispatcherGridRig();
        Assert.True((await rig.Tagging.PlaceTagAsync("QF1", GridTagKind.DoNotOperate, "hold", DispatcherGridRig.Operator, CancellationToken.None)).Accepted);
        Assert.True((await rig.Command(GridCommandType.Cancel, "QF1")).Accepted);
    }

    [Fact]
    public async Task RemovingTheTagUnblocksTheCommand()
    {
        var rig = new DispatcherGridRig();
        Assert.True((await rig.Tagging.PlaceTagAsync("QF1", GridTagKind.DoNotOperate, "hold", DispatcherGridRig.Operator, CancellationToken.None)).Accepted);
        Assert.Equal(GridCommandRejection.Interlocked, (await rig.Command(GridCommandType.SelectOpen, "QF1")).Reason);
        var tag = Assert.Single(rig.Tagging.Tags);
        Assert.True((await rig.Tagging.RemoveTagAsync(tag.Id, DispatcherGridRig.Operator, CancellationToken.None)).Accepted);
        Assert.True((await rig.Command(GridCommandType.SelectOpen, "QF1")).Accepted);
    }

    [Fact]
    public async Task TrainingForceSwitchIsNotBlockedByTags()
    {
        var rig = new DispatcherGridRig();
        Assert.True((await rig.Tagging.PlaceTagAsync("QF1", GridTagKind.DoNotOperate, "hold", DispatcherGridRig.Operator, CancellationToken.None)).Accepted);
        // Instructor/attacker training actions deliberately bypass tags (that is the point of the attack scenarios).
        var forced = await rig.Training.ForceSwitchAsync("QF1", false, DispatcherGridRig.Instructor, CancellationToken.None);
        Assert.True(forced.Accepted);
        Assert.False(rig.Engine.Snapshot().Switch("QF1")!.Closed);
    }
}
