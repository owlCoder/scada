using Application.Detection;
using global::Application.Grid;
using Domain.Grid.Snapshots;
using Security.Grid.Detection;
using Security.Grid.Detection.Rules;
using UnitTests.Security.Grid.Support;
using Xunit;

namespace UnitTests.Security.Grid.Detection.Rules;

public sealed class GridSwitchingRulesTests
{
    private static GridDetectionContext ContextOf(GridStepReport report, GridSnapshot? previous = null) =>
        new(report, previous, [], report.Timestamp, 1);

    [Fact]
    public Task UnsolicitedSwitchingFiresOnUnattributedOperation()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridUnsolicitedSwitchingRule();
        var before = view.Snapshot();
        view.SetSwitchState("SW-L01", false, GridChangeCause.Unattributed, "grid-attacker");
        view.Emit(new GridStepEvent(GridStepEventKind.SwitchOperated, "SW-L01", "Feeder 1 breaker opened directly (forced)", "grid-attacker", GridChangeCause.Unattributed));
        var report = view.Step(1);

        var findings = rule.Evaluate(ContextOf(report, before), rule.DefaultSettings);

        var finding = Assert.Single(findings);
        Assert.Equal("SW-L01", finding.Key);
        Assert.Equal("SW-L01", finding.DeviceId);

        return Task.CompletedTask;
    }

    [Fact]
    public Task UnsolicitedSwitchingStaysSilentForAcceptedOperatorCommandAndProtection()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridUnsolicitedSwitchingRule();
        var before = view.Snapshot();
        // Accepted operator command in the same step.
        view.SetSwitchState("SW-L02", false, GridChangeCause.Operator, "operator");
        view.Emit(new GridStepEvent(GridStepEventKind.CommandAccepted, "SW-L02", "Switch SW-L02 operated", "operator", GridChangeCause.Operator, "hmi"));
        view.Emit(new GridStepEvent(GridStepEventKind.SwitchOperated, "SW-L02", "Feeder 2 breaker opened by operator command", "operator", GridChangeCause.Operator, "hmi"));
        // Protection trip.
        view.SetSwitchState("SW-L03", false, GridChangeCause.Protection, null);
        view.Emit(new GridStepEvent(GridStepEventKind.SwitchOperated, "SW-L03", "Feeder 3 breaker opened by protection", null, GridChangeCause.Protection));
        var report = view.Step(1);

        Assert.Empty(rule.Evaluate(ContextOf(report, before), rule.DefaultSettings));

        return Task.CompletedTask;
    }

    [Fact]
    public Task UnsolicitedSwitchingFiresOnOperatorCauseWithoutAcceptedCommand()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridUnsolicitedSwitchingRule();
        var before = view.Snapshot();
        view.SetSwitchState("SW-L01", false, GridChangeCause.Operator, "rogue");
        view.Emit(new GridStepEvent(GridStepEventKind.SwitchOperated, "SW-L01", "opened", "rogue", GridChangeCause.Operator, "iec104"));
        var report = view.Step(1);

        Assert.Single(rule.Evaluate(ContextOf(report, before), rule.DefaultSettings));

        return Task.CompletedTask;
    }

    [Fact]
    public Task MassSwitchingFiresAtThreeOpeningsAndStaysSilentBelowThreshold()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridMassSwitchingRule();
        var settings = rule.DefaultSettings;

        for (var i = 1; i <= 2; i++)
        {
            view.SetSwitchState($"SW-L0{i}", false, GridChangeCause.Unattributed, "attacker");
            view.Emit(new GridStepEvent(GridStepEventKind.SwitchOperated, $"SW-L0{i}", "opened", "attacker", GridChangeCause.Unattributed));
            var report = view.Step(1);
            Assert.Empty(rule.Evaluate(ContextOf(report), settings));
        }

        view.SetSwitchState("SW-L03", false, GridChangeCause.Unattributed, "attacker");
        view.Emit(new GridStepEvent(GridStepEventKind.SwitchOperated, "SW-L03", "opened", "attacker", GridChangeCause.Unattributed));
        var third = view.Step(1);

        var finding = Assert.Single(rule.Evaluate(ContextOf(third), settings));
        Assert.Equal("mass-switching", finding.Key);
        Assert.Contains("SW-L01", finding.Description);
        Assert.Contains("SW-L03", finding.Description);

        return Task.CompletedTask;
    }

    [Fact]
    public Task MassSwitchingWindowExpires()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridMassSwitchingRule();
        var settings = rule.DefaultSettings with { WindowSeconds = 10 };

        for (var i = 1; i <= 3; i++)
        {
            view.SetSwitchState($"SW-L0{i}", false, GridChangeCause.Unattributed, "attacker");
            view.Emit(new GridStepEvent(GridStepEventKind.SwitchOperated, $"SW-L0{i}", "opened", "attacker", GridChangeCause.Unattributed));
            view.Step(1);
        }
        // All three openings are now older than the 10 s window.
        var report = view.Step(20);
        Assert.Empty(rule.Evaluate(ContextOf(report), settings));

        return Task.CompletedTask;
    }
}
