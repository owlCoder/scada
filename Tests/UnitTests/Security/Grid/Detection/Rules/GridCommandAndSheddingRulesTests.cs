using Application.Detection;
using Application.Events;
using global::Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;
using Security.Grid.Detection;
using Security.Grid.Detection.Rules;
using UnitTests.Security.Grid.Support;
using Xunit;

namespace UnitTests.Security.Grid.Detection.Rules;

public sealed class GridCommandAndSheddingRulesTests
{
    private static GridDetectionContext ContextOf(GridStepReport report) => new(report, null, [], report.Timestamp, 1);

    private static GridStepEvent Rejected(string target, string actor, string channel, string message) =>
        new(GridStepEventKind.CommandRejected, target, message, actor, GridChangeCause.Operator, channel);

    [Fact]
    public Task UnauthorizedCommandFiresOnNotAuthorizedReplayAndRateLimited()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridUnauthorizedCommandRule();
        var settings = rule.DefaultSettings;
        view.Emit(Rejected("SW-L01", "guest", "rest", "NotAuthorized: role Viewer may not issue grid commands."));
        var first = view.Step(1);
        Assert.Single(rule.Evaluate(ContextOf(first), settings));

        rule.ResetState();
        view.Emit(Rejected("SW-L01", "operator", "hmi", "Replay: sequence number 4 was already used by operator."));
        var replay = view.Step(1);
        Assert.Single(rule.Evaluate(ContextOf(replay), settings));

        rule.ResetState();
        view.Emit(Rejected("SW-L01", "operator", "hmi", "RateLimited: operator exceeded the command rate limit."));
        var limited = view.Step(1);
        Assert.Single(rule.Evaluate(ContextOf(limited), settings));

        return Task.CompletedTask;
    }

    [Fact]
    public Task UnauthorizedCommandIgnoresBenignRejections()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridUnauthorizedCommandRule();
        view.Emit(Rejected("SW-L01", "operator", "hmi", "AlreadyInState: switch SW-L01 is already open."));
        Assert.Empty(rule.Evaluate(ContextOf(view.Step(1)), rule.DefaultSettings));

        return Task.CompletedTask;
    }

    [Fact]
    public Task UnauthorizedCommandFiresOnIec104Burst()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridUnauthorizedCommandRule();
        var settings = rule.DefaultSettings;
        for (var i = 0; i < 8; i++)
        {
            view.Emit(new GridStepEvent(GridStepEventKind.CommandAccepted, $"LD-{i}", "shed", "iec104-rogue", GridChangeCause.Operator, "iec104"));
            var report = view.Step(1);
            if (i < 7)
                Assert.DoesNotContain(rule.Evaluate(ContextOf(report), settings), f => f.Key.StartsWith("iec104:", StringComparison.Ordinal));
            else
                Assert.Contains(rule.Evaluate(ContextOf(report), settings), f => f.Key == "iec104:iec104-rogue");
        }

        return Task.CompletedTask;
    }

    [Fact]
    public Task UnauthorizedCommandReadsRecentSecurityEvents()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridUnauthorizedCommandRule();
        var now = view.Now;
        var securityEvents = new[]
        {
            new SecurityEvent(Guid.NewGuid(), now, EventCategories.CommandRejected, EventSeverity.Medium, "grid-control",
                "NotAuthorized: role Viewer may not issue grid commands.", "guest", "SW-L01")
        };
        var report = new GridStepReport(now, view.Snapshot(), []);
        var context = new GridDetectionContext(report, null, securityEvents, now, 1);
        Assert.Single(rule.Evaluate(context, rule.DefaultSettings));

        return Task.CompletedTask;
    }

    [Fact]
    public Task LoadSheddingAnomalyFiresWithoutOperatorConsoleCommand()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridLoadSheddingAnomalyRule();
        view.SetShed("LD-F1A", true);
        view.Emit(new GridStepEvent(GridStepEventKind.LoadShed, "LD-F1A", "LD-F1A shed by command from iec104-rogue", "iec104-rogue", GridChangeCause.Operator, "iec104"));
        view.Emit(new GridStepEvent(GridStepEventKind.CommandAccepted, "LD-F1A", "shed accepted", "iec104-rogue", GridChangeCause.Operator, "iec104"));
        var report = view.Step(1);

        Assert.Contains(rule.Evaluate(ContextOf(report), rule.DefaultSettings), f => f.Key == "shed:LD-F1A");

        return Task.CompletedTask;
    }

    [Fact]
    public Task LoadSheddingAnomalyStaysSilentForHmiCommandAndProtectionTrip()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridLoadSheddingAnomalyRule();
        var before = view.Snapshot();

        view.SetShed("LD-F3B", true); // 50 customers: below the aggregate threshold
        view.Emit(new GridStepEvent(GridStepEventKind.LoadShed, "LD-F3B", "shed by operator", "operator", GridChangeCause.Operator, "hmi"));
        view.Emit(new GridStepEvent(GridStepEventKind.CommandAccepted, "LD-F3B", "shed accepted", "operator", GridChangeCause.Operator, "hmi"));
        var report = view.Step(1);
        Assert.Empty(rule.Evaluate(new GridDetectionContext(report, before, [], report.Timestamp, 1), rule.DefaultSettings));

        rule.ResetState();
        var before2 = view.Snapshot();
        view.SetShed("LD-F2A", true);
        view.Emit(new GridStepEvent(GridStepEventKind.LoadShed, "LD-F2A", "shed by protection", null, GridChangeCause.Protection));
        view.Emit(new GridStepEvent(GridStepEventKind.ProtectionTripped, "PR-L02", "trip", null, GridChangeCause.Protection));
        var report2 = view.Step(1);
        Assert.Empty(rule.Evaluate(new GridDetectionContext(report2, before2, [], report2.Timestamp, 1), rule.DefaultSettings));

        return Task.CompletedTask;
    }

    [Fact]
    public Task LoadSheddingAnomalyFiresWhenManyCustomersAreLostWithoutProtectionTrip()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridLoadSheddingAnomalyRule();
        var settings = rule.DefaultSettings with { Threshold = 100, WindowSeconds = 60 };
        foreach (var load in new[] { "LD-F1A", "LD-F1B", "LD-F2A", "LD-F2B" })
        {
            view.SetShed(load, true);
            view.Emit(new GridStepEvent(GridStepEventKind.LoadShed, load, "shed", "attacker", GridChangeCause.Unattributed));
        }
        var report = view.Step(1);
        Assert.Contains(rule.Evaluate(ContextOf(report), settings), f => f.Key == "customers");

        return Task.CompletedTask;
    }

    [Fact]
    public async Task FakeControlRejectsViewerAndReplayAndAcceptsOperator()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var control = new SecurityFakeGridControl(view);
        var viewer = new Actor("guest", AccessRole.Viewer);
        var op = new Actor("operator", AccessRole.Operator);

        var denied = await control.ExecuteAsync(new GridCommand(GridCommandType.SelectOpen, "SW-L01", null, 1), viewer, "rest", CancellationToken.None);
        Assert.False(denied.Accepted);
        Assert.Equal(GridCommandRejection.NotAuthorized, denied.Reason);

        var accepted = await control.ExecuteAsync(new GridCommand(GridCommandType.SelectOpen, "SW-L01", null, 1), op, "hmi", CancellationToken.None);
        Assert.True(accepted.Accepted);
        var replay = await control.ExecuteAsync(new GridCommand(GridCommandType.Execute, "SW-L01", null, 1), op, "hmi", CancellationToken.None);
        Assert.False(replay.Accepted);
        Assert.Equal(GridCommandRejection.Replay, replay.Reason);

        var executed = await control.ExecuteAsync(new GridCommand(GridCommandType.Execute, "SW-L01", null, 2), op, "hmi", CancellationToken.None);
        Assert.True(executed.Accepted);
        view.Step(1);
        Assert.False(view.Snapshot().Switch("SW-L01")!.Closed);
    }
}
