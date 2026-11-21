using Application.Detection;
using global::Application.Grid;
using Domain.Grid.Snapshots;
using Security.Grid.Detection;
using Security.Grid.Detection.Rules;
using UnitTests.Security.Grid.Support;
using Xunit;

namespace UnitTests.Security.Grid.Detection.Rules;

public sealed class GridMeasurementInconsistencyRuleTests
{
    private static GridDetectionContext ContextOf(GridStepReport report) => new(report, null, [], report.Timestamp, 1);

    [Fact]
    public Task SpoofedMeasurementFires()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridMeasurementInconsistencyRule(view);
        view.Step(1); // settle
        var measurementId = view.Snapshot().Measurements.First(m => m.Kind == GridMeasurementKind.Current && m.ElementId == "L-01").Id;
        view.SetSpoof(measurementId, 15);
        view.Emit(new GridStepEvent(GridStepEventKind.MeasurementSpoofed, measurementId, "spoofed", "attacker", GridChangeCause.Unattributed));
        var report = view.Step(1);

        var findings = rule.Evaluate(ContextOf(report), rule.DefaultSettings);

        var finding = Assert.Single(findings, f => f.Key == $"measurement:{measurementId}");
        // Findings become detections and incidents that operators read: they must not carry the true value.
        var trueText = view.Snapshot().Measurements.Single(m => m.Id == measurementId).TrueValue
            .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        Assert.DoesNotContain(trueText, finding.Description, StringComparison.Ordinal);
        Assert.All(finding.Evidence, e => Assert.DoesNotContain("true", e.Detail, StringComparison.OrdinalIgnoreCase));

        return Task.CompletedTask;
    }

    [Fact]
    public Task NormalNoiseStaysSilent()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default(), seed: 7);
        var rule = new GridMeasurementInconsistencyRule(view);

        for (var i = 0; i < 120; i++)
        {
            var report = view.Step(1);
            Assert.Empty(rule.Evaluate(ContextOf(report), rule.DefaultSettings));
        }

        return Task.CompletedTask;
    }

    [Fact]
    public Task BusBalanceResidualFiresWhenReportedFlowDoesNotMatchLoad()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridMeasurementInconsistencyRule(view);
        view.Step(1);
        // Spoof the feeder power measurement to hide the load (false data injection).
        var powerMeasurement = view.Snapshot().Measurements.First(m => m.Kind == GridMeasurementKind.ActivePower && m.ElementId == "L-01");
        var trueValue = powerMeasurement.TrueValue;
        view.SetSpoof(powerMeasurement.Id, trueValue * 0.3);
        var report = view.Step(1);

        var findings = rule.Evaluate(ContextOf(report), rule.DefaultSettings);

        Assert.Contains(findings, f => f.Key.StartsWith("bus:", StringComparison.Ordinal));

        return Task.CompletedTask;
    }

    [Fact]
    public Task BadQualityMeasurementsAreNotTreatedAsInconsistency()
    {
        var view = new SecurityFakeGridView(SecurityFakeGridNetworks.Default());
        var rule = new GridMeasurementInconsistencyRule(view);
        view.SetRtuOnline("RTU-2", false);
        view.Emit(new GridStepEvent(GridStepEventKind.RtuOnlineChanged, "RTU-2", "RTU RTU-2 offline", "attacker", GridChangeCause.Unattributed));
        var report = view.Step(1);

        Assert.Empty(rule.Evaluate(ContextOf(report), rule.DefaultSettings));

        return Task.CompletedTask;
    }
}
