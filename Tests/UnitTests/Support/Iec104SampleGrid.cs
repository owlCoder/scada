using Domain.Grid.Definition;
using Domain.Grid.Snapshots;
using Domain.Process.Devices;

namespace UnitTests.Support;

/// <summary>
/// A small deterministic grid definition and matching snapshot used by the IEC 104 unit tests: two RTUs, one line,
/// one OLTC transformer, one switch, one load per RTU side, a generator, a capacitor and a protection relay.
/// </summary>
public static class Iec104SampleGrid
{
    public const string Rtu1Id = "RTU-1";
    public const string Rtu2Id = "RTU-2";
    public const int Rtu1CommonAddress = 1;
    public const int Rtu2CommonAddress = 2;
    public const string LineId = "L1";
    public const string SwitchId = "SW1";
    public const string TransformerId = "TR1";
    public const string Load1Id = "LD1";
    public const string Load2Id = "LD2";
    public const string GeneratorId = "G1";
    public const string CapacitorId = "C1";
    public const string ProtectionId = "P1";
    public const string BusHvId = "B-HV";
    public const string BusLvId = "B-LV";

    public static GridDefinition CreateDefinition() =>
        new("Sample grid", 50.0,
            Buses:
            [
                new GridBusDefinition(BusHvId, "HV bus", 110, GridBusKind.Busbar, new DiagramPosition(0, 0)),
                new GridBusDefinition(BusLvId, "LV bus", 10, GridBusKind.Busbar, new DiagramPosition(4, 0))
            ],
            Sources: [new GridSourceDefinition("S1", "Grid infeed", BusHvId, 1.02, 500, new DiagramPosition(-4, 0))],
            Lines:
            [
                new GridLineDefinition(LineId, "Feeder", BusHvId, BusLvId, GridLineKind.Overhead, 12, 0.1, 0.4, 3, 400,
                    null, [])
            ],
            Transformers:
            [
                new GridTransformerDefinition(TransformerId, "Transformer", BusHvId, BusLvId, 20, 110, 10, 8, 40, 2,
                    true, -5, 5, 0, 2, 1.5, 10.0, 2.0, 30, "2w", new DiagramPosition(2, 0))
            ],
            Switches:
            [
                new GridSwitchDefinition(SwitchId, "Breaker", GridSwitchKind.Breaker, BusLvId, LineId, true, 25,
                    new DiagramPosition(3, 0))
            ],
            Loads:
            [
                new GridLoadDefinition(Load1Id, "Load 1", BusLvId, 5, 0.95, GridLoadProfile.Industrial, 100, false,
                    new DiagramPosition(5, 1)),
                new GridLoadDefinition(Load2Id, "Load 2", BusLvId, 3, 0.9, GridLoadProfile.Residential, 50, false,
                    new DiagramPosition(5, -1))
            ],
            Generators: [new GridGeneratorDefinition(GeneratorId, "PV", BusLvId, GridGeneratorKind.Photovoltaic, 2,
                new DiagramPosition(6, 1))],
            Capacitors: [new GridCapacitorDefinition(CapacitorId, "Cap bank", BusLvId, 1.2, 4, 2, new DiagramPosition(6, -1))],
            Protections:
            [
                new GridProtectionDefinition(ProtectionId, "Overcurrent", SwitchId, GridProtectionFunction.Overcurrent51,
                    GridCurveType.IecStandardInverse, 300, 0.5, true)
            ],
            Rtus:
            [
                new GridRtuDefinition(Rtu1Id, "RTU 1", Rtu1CommonAddress, null, [LineId, SwitchId, Load1Id, BusLvId]),
                new GridRtuDefinition(Rtu2Id, "RTU 2", Rtu2CommonAddress, null,
                    [TransformerId, Load2Id, GeneratorId, CapacitorId, ProtectionId])
            ]);

    public static GridSnapshot CreateSnapshot(double lineCurrentA = 120, double busVoltageKv = 10.2, bool switchClosed = true,
        int tap = 3, bool tripped = false)
    {
        var measurements = new List<GridMeasurementSnapshot>
        {
            Measurement("M-L1-I", Rtu1Id, LineId, GridMeasurementKind.Current, "A", lineCurrentA),
            Measurement("M-L1-P", Rtu1Id, LineId, GridMeasurementKind.ActivePower, "MW", 2.0),
            Measurement("M-L1-Q", Rtu1Id, LineId, GridMeasurementKind.ReactivePower, "Mvar", 0.4),
            Measurement("M-LD1-P", Rtu1Id, Load1Id, GridMeasurementKind.ActivePower, "MW", 4.5),
            Measurement("M-LD1-Q", Rtu1Id, Load1Id, GridMeasurementKind.ReactivePower, "Mvar", 1.1),
            Measurement("M-B-LV-V", Rtu1Id, BusLvId, GridMeasurementKind.Voltage, "kV", busVoltageKv),
            Measurement("M-TR1-I", Rtu2Id, TransformerId, GridMeasurementKind.Current, "A", 80),
            Measurement("M-TR1-P", Rtu2Id, TransformerId, GridMeasurementKind.ActivePower, "MW", 1.5),
            Measurement("M-TR1-Q", Rtu2Id, TransformerId, GridMeasurementKind.ReactivePower, "Mvar", 0.3),
            Measurement("M-LD2-P", Rtu2Id, Load2Id, GridMeasurementKind.ActivePower, "MW", 2.5),
            Measurement("M-LD2-Q", Rtu2Id, Load2Id, GridMeasurementKind.ReactivePower, "Mvar", 0.8),
            Measurement("M-G1-P", Rtu2Id, GeneratorId, GridMeasurementKind.ActivePower, "MW", 1.2),
            Measurement("M-G1-Q", Rtu2Id, GeneratorId, GridMeasurementKind.ReactivePower, "Mvar", 0.1),
            Measurement("M-C1-Q", Rtu2Id, CapacitorId, GridMeasurementKind.ReactivePower, "Mvar", 0.6)
        };
        return new GridSnapshot("Sample grid", 1, TimeSpan.FromSeconds(1), 1, 50.0, true, 5,
            Buses:
            [
                new BusSnapshot(BusHvId, "HV bus", 110, true, 112.1, 1.019, 0),
                new BusSnapshot(BusLvId, "LV bus", 10, true, busVoltageKv, busVoltageKv / 10, 0.5)
            ],
            Branches:
            [
                new BranchSnapshot(LineId, "Feeder", GridBranchKind.Line, BusHvId, BusLvId, true, lineCurrentA, 2.0, 0.4,
                    2.0, 0.4, 0.01, 30, null, null, GridEquipmentFault.None),
                new BranchSnapshot(TransformerId, "Transformer", GridBranchKind.Transformer, BusHvId, BusLvId, true, 80, 1.5,
                    0.3, 1.5, 0.3, 0.02, 10, tap, true, GridEquipmentFault.None)
            ],
            Switches:
            [
                new SwitchSnapshot(SwitchId, "Breaker", GridSwitchKind.Breaker, BusLvId, LineId, switchClosed, false, null,
                    null, GridChangeCause.Operator, GridEquipmentFault.None)
            ],
            Loads: [new LoadSnapshot(Load1Id, "Load 1", BusLvId, true, 4.5, 4.5, 1.1, 100, 100, false, false)],
            Generators: [new GeneratorSnapshot(GeneratorId, "PV", BusLvId, GridGeneratorKind.Photovoltaic, true, 1.2, 0.1)],
            Capacitors: [new CapacitorSnapshot(CapacitorId, "Cap bank", BusLvId, 2, 4, 0.6)],
            Protections:
            [
                new ProtectionSnapshot(ProtectionId, "Overcurrent", SwitchId, GridProtectionFunction.Overcurrent51,
                    GridCurveType.IecStandardInverse, 300, 0.5, true, false, tripped, null, false)
            ],
            Measurements: measurements,
            Alarms: [],
            Totals: new GridTotals(7, 1.2, 8, 0.03, 150, 150));
    }

    private static GridMeasurementSnapshot Measurement(string id, string rtuId, string elementId, GridMeasurementKind kind,
        string unit, double reported) =>
        new(id, elementId, rtuId, kind, unit, reported, MeasurementQuality.Good, reported, false, null);
}
