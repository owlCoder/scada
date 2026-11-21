using Domain.Grid.Snapshots;
using Domain.Process.Devices;
using Protocols.Iec104;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Protocols.Iec104;

/// <summary>
/// The point evaluator must read the operator view: reported measurement values and reported switch/tap states, never
/// true values, spoofing indicators or injected faults — exactly what an IEC 104 master may see.
/// </summary>
public sealed class Iec104PointEvaluatorTests
{
    private static Iec104AddressMap Map => Iec104AddressMap.Build(Iec104SampleGrid.CreateDefinition());

    [Fact]
    public Task EvaluateWithDefinitionReportsSpoofedSwitchAndTapStatesNotGroundTruth()
    {
        var definition = Iec104SampleGrid.CreateDefinition();
        var map = Iec104AddressMap.Build(definition);
        // Ground truth: breaker open, tap 3. The RTUs report closed and tap 1 (spoofed).
        var truth = Iec104SampleGrid.CreateSnapshot(switchClosed: false, tap: 3);
        var snapshot = truth with
        {
            Measurements =
            [
                .. truth.Measurements,
                new GridMeasurementSnapshot("M-SW1-S", Iec104SampleGrid.SwitchId, Iec104SampleGrid.Rtu1Id,
                    GridMeasurementKind.SwitchState, "", 1, MeasurementQuality.Good, 0, true, 1),
                new GridMeasurementSnapshot("M-TR1-T", Iec104SampleGrid.TransformerId, Iec104SampleGrid.Rtu2Id,
                    GridMeasurementKind.TapPosition, "step", 1, MeasurementQuality.Good, 3, true, 1)
            ]
        };

        var switchPoint = map.Rtu(1)!.Points.Single(p => p.Kind == Iec104PointKind.SwitchStatus);
        var tapPoint = map.Rtu(2)!.Points.Single(p => p.Kind == Iec104PointKind.TapPosition);

        // Without a definition the raw snapshot is used as-is (callers pass the redacted view).
        Assert.Equal(Iec104ElementCodec.DoublePointOpen,
            Iec104PointEvaluator.Evaluate(switchPoint, snapshot)!.DoublePoint);
        // With the definition the snapshot is projected through GridViewRedactor.ForOperators: reported values win.
        Assert.Equal(Iec104ElementCodec.DoublePointClosed,
            Iec104PointEvaluator.Evaluate(switchPoint, snapshot, definition)!.DoublePoint);
        Assert.Equal(1, Iec104PointEvaluator.Evaluate(tapPoint, snapshot, definition)!.Step);
        Assert.Equal(3, Iec104PointEvaluator.Evaluate(tapPoint, snapshot)!.Step);
        return Task.CompletedTask;
    }

    [Fact]
    public Task MeasurementsUseReportedValueAndQualityNeverTrueValue()
    {
        var definition = Iec104SampleGrid.CreateDefinition();
        var map = Iec104AddressMap.Build(definition);
        var truth = Iec104SampleGrid.CreateSnapshot();
        var spoofed = truth.Measurements
            .Select(m => m.Id == "M-B-LV-V"
                ? m with { ReportedValue = 9.9, TrueValue = 10.2, IsSpoofed = true, SpoofedValue = 9.9 }
                : m)
            .ToArray();
        var snapshot = truth with { Measurements = spoofed };

        var voltagePoint = map.Rtu(1)!.Points.Single(p => p.ElementId == Iec104SampleGrid.BusLvId && p.MeasurementKind == GridMeasurementKind.Voltage);
        var sample = Iec104PointEvaluator.Evaluate(voltagePoint, snapshot, definition)!;
        Assert.Equal(9.9, sample.Measurement);
        Assert.Equal(0, sample.Quality); // Good quality: the spoof itself is not visible on the wire
        // The same point without a definition also reads the reported value (the snapshot is already the operator view).
        Assert.Equal(9.9, Iec104PointEvaluator.Evaluate(voltagePoint, snapshot)!.Measurement);
        return Task.CompletedTask;
    }
}
