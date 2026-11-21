using Application.Detection;
using global::Application.Grid;
using Domain.Grid.Snapshots;
using Security.Grid.Detection;
using Security.Grid.Detection.Rules;
using UnitTests.Security.Grid.Support;
using Xunit;

namespace UnitTests.Security.Grid.Detection.Rules;

public sealed class GridPhysicalRulesTests
{
    private static GridDetectionContext ContextOf(GridStepReport report, GridSnapshot? previous = null) =>
        new(report, previous, [], report.Timestamp, 1);

    [Fact]
    public Task ProtectionTamperingFiresOnSettingChangeEventAndSnapshotFlag()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridProtectionTamperingRule(view);
        view.SetProtection("PR-L01", 99999, 99, false);
        view.Emit(new GridStepEvent(GridStepEventKind.ProtectionSettingChanged, "PR-L01", "pickup=99999 enabled=False", "attacker", GridChangeCause.Unattributed));
        var report = view.Step(1);

        var finding = Assert.Single(rule.Evaluate(ContextOf(report), rule.DefaultSettings));
        Assert.Equal("PR-L01", finding.Key);
        Assert.Equal(DetectionSeverity.Critical, rule.DefaultSettings.Severity);

        return Task.CompletedTask;
    }

    [Fact]
    public Task ProtectionTamperingStaysSilentWhenUntouched()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridProtectionTamperingRule(view);
        for (var i = 0; i < 10; i++)
            Assert.Empty(rule.Evaluate(ContextOf(view.Step(1)), rule.DefaultSettings));

        return Task.CompletedTask;
    }

    [Fact]
    public Task TapManipulationFiresOnRapidChangesAndOnLimitWithVoltageViolation()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridTapManipulationRule(view);
        for (var i = 0; i < 3; i++)
        {
            view.SetTap("TR-01", 8, automatic: false);
            view.Emit(new GridStepEvent(GridStepEventKind.TapChanged, "TR-01", "tap 8 (forced to max)", "attacker", GridChangeCause.Unattributed));
            var report = view.Step(1);
            var findings = rule.Evaluate(ContextOf(report), rule.DefaultSettings);
            if (i == 2)
            {
                Assert.Contains(findings, f => f.Key == "TR-01:rapid");
                Assert.Contains(findings, f => f.Key == "TR-01:limit");
            }
        }

        return Task.CompletedTask;
    }

    [Fact]
    public Task TapManipulationStaysSilentForAutomaticChanges()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridTapManipulationRule(view);
        view.Emit(new GridStepEvent(GridStepEventKind.TapChanged, "TR-01", "AVR raised tap to 1", null, GridChangeCause.Automatic));
        Assert.Empty(rule.Evaluate(ContextOf(view.Step(1)), rule.DefaultSettings));

        return Task.CompletedTask;
    }

    [Fact]
    public Task VoltageViolationFiresOnlyWhenSustainedAndSeverityFollowsDeviation()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridVoltageViolationRule(view);
        var settings = rule.DefaultSettings; // window 60 s, step 1 s

        view.SetTap("TR-01", 8, automatic: false); // +12 % on the LV board
        for (var i = 0; i < 59; i++)
        {
            var report = view.Step(1);
            Assert.Empty(rule.Evaluate(ContextOf(report), settings));
        }

        var sustained = view.Step(1);
        var findings = rule.Evaluate(ContextOf(sustained), settings);
        var finding = Assert.Single(findings);
        Assert.Equal("BUS-LV", finding.Key);
        Assert.Null(finding.SeverityOverride); // deviation ~0.012 pu is below the 0.05 High threshold

        return Task.CompletedTask;
    }

    [Fact]
    public Task VoltageViolationSeverityEscalatesWithDeviation()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridVoltageViolationRule(view);
        var settings = rule.DefaultSettings with { WindowSeconds = 2 };
        var over = view.Snapshot() with
        {
            Buses = view.Snapshot().Buses.Select(b => b.Id == "BUS-F2" ? b with { VoltagePu = 1.16, VoltageKv = 11.6, Energized = true } : b).ToArray()
        };
        var previous = view.Snapshot();
        for (var i = 0; i < 2; i++)
        {
            var report = new GridStepReport(view.Now.AddSeconds(i + 1), over, []);
            rule.Evaluate(new GridDetectionContext(report, previous, [], report.Timestamp, 1), settings);
            previous = over;
        }
        var last = new GridStepReport(view.Now.AddSeconds(3), over, []);
        var finding = Assert.Single(rule.Evaluate(new GridDetectionContext(last, previous, [], last.Timestamp, 1), settings));
        Assert.Equal(DetectionSeverity.High, finding.SeverityOverride); // 0.06 pu beyond the 1.10 limit

        return Task.CompletedTask;
    }

    [Fact]
    public Task VoltageViolationStaysSilentInNormalOperation()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default(), seed: 3);
        var rule = new GridVoltageViolationRule(view);
        for (var i = 0; i < 200; i++)
            Assert.Empty(rule.Evaluate(ContextOf(view.Step(1)), rule.DefaultSettings));

        return Task.CompletedTask;
    }

    [Fact]
    public Task OverloadFiresOnCraftedSustainedOverload()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridOverloadRule();
        var settings = rule.DefaultSettings with { WindowSeconds = 3 };
        var overloaded = view.Snapshot() with
        {
            Branches = view.Snapshot().Branches.Select(b => b.Id == "L-01" ? b with { LoadingPercent = 125, Energized = true } : b).ToArray()
        };
        var previous = view.Snapshot();
        for (var i = 0; i < 3; i++)
        {
            var report = new GridStepReport(view.Now.AddSeconds(i + 1), overloaded, []);
            rule.Evaluate(new GridDetectionContext(report, previous, [], report.Timestamp, 1), settings);
            previous = overloaded;
        }
        var last = new GridStepReport(view.Now.AddSeconds(4), overloaded, []);
        var findings = rule.Evaluate(new GridDetectionContext(last, previous, [], last.Timestamp, 1), settings);
        var finding = Assert.Single(findings);
        Assert.Equal("L-01", finding.Key);
        Assert.Equal(DetectionSeverity.High, finding.SeverityOverride); // 125 % > 120 %

        return Task.CompletedTask;
    }

    [Fact]
    public Task RtuCommunicationLossFiresWhenWholeRtuIsBadOrOffline()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridRtuCommunicationLossRule();
        view.SetRtuOnline("RTU-1", false);
        view.Emit(new GridStepEvent(GridStepEventKind.RtuOnlineChanged, "RTU-1", "RTU RTU-1 offline", "attacker", GridChangeCause.Unattributed));
        var report = view.Step(1);

        var finding = Assert.Single(rule.Evaluate(ContextOf(report), rule.DefaultSettings));
        Assert.Equal("RTU-1", finding.Key);

        return Task.CompletedTask;
    }

    [Fact]
    public Task RtuCommunicationLossStaysSilentWhenOneMeasurementIsUncertain()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridRtuCommunicationLossRule();
        var snapshot = view.Step(1).Snapshot with
        {
            Measurements = view.Snapshot().Measurements.Select(m => m.RtuId == "RTU-1" && m.Kind == GridMeasurementKind.Current
                ? m with { Quality = global::Domain.Process.Devices.MeasurementQuality.Uncertain } : m).ToArray()
        };
        var report = new GridStepReport(view.Now, snapshot, []);
        Assert.Empty(rule.Evaluate(ContextOf(report), rule.DefaultSettings));

        return Task.CompletedTask;
    }
}
