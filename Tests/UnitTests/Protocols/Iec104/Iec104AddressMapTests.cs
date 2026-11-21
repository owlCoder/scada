using Domain.Grid.Definition;
using Domain.Grid.Snapshots;
using Protocols.Iec104;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Protocols.Iec104;

public sealed class Iec104AddressMapTests
{
    private static Iec104AddressMap Map => Iec104AddressMap.Build(Iec104SampleGrid.CreateDefinition());

    [Fact]
    public Task MapsEveryRtuWithItsCommonAddress()
    {
        var map = Map;
        Assert.Equal(2, map.Rtus.Count);
        Assert.Equal(Iec104SampleGrid.Rtu1CommonAddress, map.Rtus[0].CommonAddress);
        Assert.Equal(Iec104SampleGrid.Rtu2CommonAddress, map.Rtus[1].CommonAddress);
        Assert.Equal(Iec104SampleGrid.Rtu1Id, map.Rtu(1)!.RtuId);
        Assert.Null(map.Rtu(99));
        return Task.CompletedTask;
    }

    [Fact]
    public Task AssignsIoaRangesPerKind()
    {
        var rtu1 = Map.Rtu(1)!;
        Assert.Equal(
            [
                Iec104PointKind.Measurement, Iec104PointKind.Measurement, Iec104PointKind.Measurement, // L1: I, P, Q
                Iec104PointKind.Measurement, Iec104PointKind.Measurement,                               // LD1: P, Q
                Iec104PointKind.Measurement,                                                            // B-LV: V
                Iec104PointKind.SwitchStatus,                                                           // SW1
                Iec104PointKind.RtuStatus
            ],
            rtu1.Points.Select(p => p.Kind));
        Assert.Equal([1000, 1001, 1002, 1003, 1004, 1005], rtu1.Points.Take(6).Select(p => p.InformationObjectAddress));
        Assert.Equal(2000, rtu1.Points.Single(p => p.Kind == Iec104PointKind.SwitchStatus).InformationObjectAddress);
        Assert.Equal(5000, rtu1.Points.Single(p => p.Kind == Iec104PointKind.RtuStatus).InformationObjectAddress);

        var rtu2 = Map.Rtu(2)!;
        Assert.Equal(3000, rtu2.Points.Single(p => p.Kind == Iec104PointKind.TapPosition).InformationObjectAddress);
        Assert.Equal(4000, rtu2.Points.Single(p => p.Kind == Iec104PointKind.ProtectionTrip).InformationObjectAddress);
        Assert.Equal(5000, rtu2.Points.Single(p => p.Kind == Iec104PointKind.RtuStatus).InformationObjectAddress);
        Assert.Equal(8, rtu2.Points.Count(p => p.Kind == Iec104PointKind.Measurement)); // TR1 I/P/Q, LD2 P/Q, G1 P/Q, C1 Q
        return Task.CompletedTask;
    }

    [Fact]
    public Task CommandsUseTheSameIoaAsTheStatusPoint()
    {
        var map = Map;
        var switchPoint = map.Point(1, 2000)!;
        Assert.Equal(Iec104PointKind.SwitchStatus, switchPoint.Kind);
        Assert.True(switchPoint.Controllable);
        Assert.Equal(Iec104SampleGrid.SwitchId, switchPoint.ElementId);
        var tapPoint = map.Point(2, 3000)!;
        Assert.Equal(Iec104PointKind.TapPosition, tapPoint.Kind);
        Assert.True(tapPoint.Controllable);
        Assert.Equal(Iec104SampleGrid.TransformerId, tapPoint.ElementId);
        Assert.False(map.Point(1, 1000)!.Controllable);
        Assert.False(map.Point(2, 4000)!.Controllable);
        Assert.Null(map.Point(1, 9999));
        Assert.Null(map.Point(99, 1000));
        return Task.CompletedTask;
    }

    [Fact]
    public Task MapIsDeterministicAcrossBuilds()
    {
        var first = Iec104AddressMap.Build(Iec104SampleGrid.CreateDefinition());
        var second = Iec104AddressMap.Build(Iec104SampleGrid.CreateDefinition());
        Assert.Equal(
            first.Rtus.SelectMany(r => r.Points).Select(p => (p.CommonAddress, p.InformationObjectAddress, p.Kind, p.ElementId, p.MeasurementKind)),
            second.Rtus.SelectMany(r => r.Points).Select(p => (p.CommonAddress, p.InformationObjectAddress, p.Kind, p.ElementId, p.MeasurementKind)));
        return Task.CompletedTask;
    }

    [Fact]
    public Task DuplicateCommonAddressThrows()
    {
        var definition = Iec104SampleGrid.CreateDefinition() with
        {
            Rtus = [.. Iec104SampleGrid.CreateDefinition().Rtus, new GridRtuDefinition("RTU-3", "dup", 1, null, [])]
        };
        Assert.Throws<InvalidOperationException>(() => Iec104AddressMap.Build(definition));
        return Task.CompletedTask;
    }

    [Fact]
    public Task UnknownElementIdsAreSkipped()
    {
        var definition = Iec104SampleGrid.CreateDefinition() with
        {
            Rtus = [new GridRtuDefinition("RTU-9", "RTU 9", 9, null, ["no-such-element", Iec104SampleGrid.SwitchId])]
        };
        var map = Iec104AddressMap.Build(definition);
        var rtu = map.Rtu(9)!;
        Assert.Equal(2, rtu.Points.Count); // switch status + RTU status only
        Assert.Equal(Iec104PointKind.SwitchStatus, rtu.Points[0].Kind);
        return Task.CompletedTask;
    }

    [Fact]
    public Task MeasurementRangesComeFromElementRatings()
    {
        var map = Map;
        var voltage = map.Point(1, 1005)!;
        Assert.Equal(GridMeasurementKind.Voltage, voltage.MeasurementKind);
        Assert.Equal(10, voltage.Range); // LV bus nominal voltage
        var current = map.Point(1, 1000)!;
        Assert.Equal(GridMeasurementKind.Current, current.MeasurementKind);
        Assert.Equal(400, current.Range); // line rated current
        return Task.CompletedTask;
    }

    [Fact]
    public Task EvaluatorReadsReportedValuesAndSnapshotStates()
    {
        var map = Map;
        var snapshot = Iec104SampleGrid.CreateSnapshot(lineCurrentA: 121, busVoltageKv: 10.4, switchClosed: false, tap: 4, tripped: true);
        var current = Iec104PointEvaluator.Evaluate(map.Point(1, 1000)!, snapshot)!;
        Assert.Equal(121, current.Measurement);
        var voltage = Iec104PointEvaluator.Evaluate(map.Point(1, 1005)!, snapshot)!;
        Assert.Equal(10.4, voltage.Measurement);
        var switchStatus = Iec104PointEvaluator.Evaluate(map.Point(1, 2000)!, snapshot)!;
        Assert.Equal(Iec104ElementCodec.DoublePointOpen, switchStatus.DoublePoint);
        var tap = Iec104PointEvaluator.Evaluate(map.Point(2, 3000)!, snapshot)!;
        Assert.Equal(4, tap.Step);
        var trip = Iec104PointEvaluator.Evaluate(map.Point(2, 4000)!, snapshot)!;
        Assert.Equal(1, trip.SinglePoint);
        var rtuStatus = Iec104PointEvaluator.Evaluate(map.Point(1, 5000)!, snapshot)!;
        Assert.Equal(1, rtuStatus.SinglePoint);
        return Task.CompletedTask;
    }

    [Fact]
    public Task DeadbandIsHalfPercentOfRange()
    {
        var before = new Iec104Sample(Iec104PointKind.Measurement, 10.0, null, null, null, false, 0);
        var within = new Iec104Sample(Iec104PointKind.Measurement, 10.04, null, null, null, false, 0);
        var beyond = new Iec104Sample(Iec104PointKind.Measurement, 10.06, null, null, null, false, 0);
        Assert.False(within.ExceedsDeadband(before, 10));
        Assert.True(beyond.ExceedsDeadband(before, 10));
        Assert.False(within.SameValue(beyond));
        Assert.True(within.SameValue(new Iec104Sample(Iec104PointKind.Measurement, 10.04, null, null, null, false, 0)));
        return Task.CompletedTask;
    }
}
