using Application.Commands;
using Application.Events;
using Application.Identity;
using Domain.Process.Commands;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Application.Commands;

public sealed class ControlCommandServiceTests
{
    private static readonly Actor Ana = new("ana", AccessRole.Operator);

    private static OperatorCommand Command(string device, OperatorCommandType type, double? value = null, long? sequence = null) =>
        new(device, type, value, CommandSource.RestApi, sequence);

    [Fact]
    public Task ViewerCannotIssueCommandsAndTheAttemptIsAudited()
    {
        var rig = new TestRig();

        var result = rig.Commands.Execute(Command("P-101", OperatorCommandType.StartPump), new Actor("guest", AccessRole.Viewer));

        Assert.Equal(CommandRejectionReason.NotAuthorized, result.Reason);
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Category == EventCategories.AuthorizationDenied && e.Actor == "guest");
        return Task.CompletedTask;
    }

    [Fact]
    public Task AcceptedCommandIsAppliedAndAuditedWithTheVerifiedActor()
    {
        var rig = new TestRig();

        Assert.True(rig.Commands.Execute(Command("V-301", OperatorCommandType.SetManual), Ana).Accepted);
        var result = rig.Commands.Execute(Command("V-301", OperatorCommandType.SetValvePosition, 25), Ana);

        Assert.True(result.Accepted, result.Message);
        Assert.Equal(25, rig.Training.Snapshot().Valve("V-301")!.Setpoint);
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Category == EventCategories.Audit && e.Actor == "ana" && e.DeviceId == "V-301"
            && e.Message.StartsWith("Set V-301 to 25%", StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    [Fact]
    public Task CommandInAutomaticModeIsRejectedAsModeMismatch()
    {
        var rig = new TestRig();
        var result = rig.Commands.Execute(Command("P-101", OperatorCommandType.StartPump), Ana);
        Assert.Equal(CommandRejectionReason.ModeMismatch, result.Reason);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ReplayedSequenceNumberIsRejected()
    {
        var rig = new TestRig();
        Assert.True(rig.Commands.Execute(Command("P-101", OperatorCommandType.SetManual, sequence: 5), Ana).Accepted);

        var replay = rig.Commands.Execute(Command("P-101", OperatorCommandType.SetAutomatic, sequence: 5), Ana);
        var older = rig.Commands.Execute(Command("P-101", OperatorCommandType.SetAutomatic, sequence: 4), Ana);

        Assert.Equal(CommandRejectionReason.ReplayDetected, replay.Reason);
        Assert.Equal(CommandRejectionReason.ReplayDetected, older.Reason);
        Assert.True(rig.Commands.Execute(Command("P-101", OperatorCommandType.SetAutomatic, sequence: 6), Ana).Accepted);
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Category == EventCategories.SecurityAlert && e.Message.Contains("(ReplayDetected)"));
        return Task.CompletedTask;
    }

    [Fact]
    public Task ExcessiveCommandsFromOneActorAreRateLimited()
    {
        var rig = new TestRig();
        for (var i = 0; i < 10; i++)
            rig.Commands.Execute(Command("V-301", OperatorCommandType.SetManual), Ana);

        Assert.Equal(CommandRejectionReason.RateLimited, rig.Commands.Execute(Command("V-301", OperatorCommandType.SetManual), Ana).Reason);
        Assert.True(rig.Commands.Execute(Command("V-301", OperatorCommandType.SetManual), new Actor("bob", AccessRole.Operator)).Accepted);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CommandIsRejectedWhenTheControllerCannotBeReached()
    {
        var rig = new TestRig();
        rig.Training.SetLinkConnected("L-SCADA-PLC1", false);
        await rig.StepAsync();

        var result = rig.Commands.Execute(Command("P-101", OperatorCommandType.SetManual), Ana);

        Assert.Equal(CommandRejectionReason.CommunicationUnavailable, result.Reason);
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Category == EventCategories.Communication && e.DeviceId == "P-101");
    }

    [Fact]
    public Task UnknownDeviceAndInvalidValuesAreRejected()
    {
        var rig = new TestRig();
        Assert.Equal(CommandRejectionReason.UnknownDevice, rig.Commands.Execute(Command("X-1", OperatorCommandType.StartPump), Ana).Reason);
        Assert.Equal(CommandRejectionReason.InvalidValue,
            rig.Commands.Execute(Command("V-301", (OperatorCommandType)99), Ana).Reason);
        Assert.Equal(CommandRejectionReason.NotAuthorized,
            rig.Commands.Execute(Command("V-301", OperatorCommandType.SetManual), new Actor(" ", AccessRole.Operator)).Reason);
        return Task.CompletedTask;
    }
}
