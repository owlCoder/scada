using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;
using Domain.Process.Alarms;
using Domain.Process.Devices;

namespace FrontendTests.Support;

public sealed class HmiFakeGridView : IGridView
{
    public GridDefinition Definition { get; } = new("Training grid", 50,
        [new("B-HV", "HV bus", 110, GridBusKind.Busbar, new(2, 2)), new("B-MV", "MV bus", 20, GridBusKind.Busbar, new(2, 7)), new("B-END", "Feeder end", 20, GridBusKind.Junction, new(10, 7))],
        [new("SRC", "Grid source", "B-HV", 1, 1000, new(2, 0))],
        [new("L1", "North feeder", "B-MV", "B-END", GridLineKind.Cable, 2, .125, .09, 300, 400, "Al XLPE 3×240 mm²", [new(6, 7)])],
        [new("T1", "Main transformer", "B-HV", "B-MV", 20, 110, 20, 12, 100, 18, true, -8, 8, 0, 0, 1.25, 20, 1, 30, null, new(2, 4))],
        [new("Q1", "Feeder breaker", GridSwitchKind.Breaker, "B-MV", "L1", true, 20, new(4, 7)), new("QS1", "Isolator", GridSwitchKind.Disconnector, "B-END", "L1", false, null, new(8, 7)), new("QL1", "Load switch", GridSwitchKind.LoadBreakSwitch, "B-END", "LD1", true, null, new(10, 9))],
        [new("LD1", "Town load", "B-END", 1.5, .95, GridLoadProfile.Residential, 900, false, new(10, 11))],
        [new("PV1", "Solar farm", "B-END", GridGeneratorKind.Photovoltaic, .4, new(13, 7))],
        [new("C1", "Capacitor bank", "B-MV", .5, 4, 1, new(5, 10))],
        [new("P1", "Feeder protection", "Q1", GridProtectionFunction.Overcurrent51, GridCurveType.IecStandardInverse, 400, .1, true)],
        [new("RTU1", "Station RTU", 1, null, ["B-MV", "L1", "Q1", "T1"])]);
    public GridStatus Status => new(false, State.SimulationTime, State.StepCount, .1, 1, Definition.Name, 1);
    public GridSnapshot State { get; set; } = new("Training grid", 1, TimeSpan.FromSeconds(25), 250, 50, true, 3,
        [new("B-HV", "HV bus", 110, true, 110, 1, 0), new("B-MV", "MV bus", 20, true, 20.2, 1.01, 0), new("B-END", "Feeder end", 20, false, 0, 0, 0)],
        [new("L1", "North feeder", GridBranchKind.Line, "B-MV", "B-END", true, 80, 1.5, .2, -1.49, -.19, .01, 20, null, null, GridEquipmentFault.EarthFault), new("T1", "Main transformer", GridBranchKind.Transformer, "B-HV", "B-MV", true, 90, 1.6, .2, -1.5, -.18, .1, 42, 0, false, GridEquipmentFault.Overheating)],
        [new("Q1", "Feeder breaker", GridSwitchKind.Breaker, "B-MV", "L1", true, false, null, "eva", GridChangeCause.Operator, GridEquipmentFault.SwitchStuck), new("QS1", "Isolator", GridSwitchKind.Disconnector, "B-END", "L1", false, false, null, null, GridChangeCause.System, GridEquipmentFault.None), new("QL1", "Load switch", GridSwitchKind.LoadBreakSwitch, "B-END", "LD1", true, false, null, null, GridChangeCause.System, GridEquipmentFault.None)],
        [new("LD1", "Town load", "B-END", true, 1.5, 1.5, .3, 900, 900, false, false)],
        [new("PV1", "Solar farm", "B-END", GridGeneratorKind.Photovoltaic, true, .3, 0)],
        [new("C1", "Capacitor bank", "B-MV", 1, 4, .125)],
        [new("P1", "Feeder protection", "Q1", GridProtectionFunction.Overcurrent51, GridCurveType.IecStandardInverse, 400, .1, true, false, true, TimeSpan.FromSeconds(10), true)],
        [new("V1", "B-MV", "RTU1", GridMeasurementKind.Voltage, "kV", 19.4, MeasurementQuality.Uncertain, 20.2, true, 19.4), new("I1", "L1", "RTU1", GridMeasurementKind.Current, "A", 70, MeasurementQuality.Good, 80, true, 70)],
        [new("AL1", "B-MV", GridAlarmType.Undervoltage, AlarmSeverity.High, "Bus voltage low", TimeSpan.FromSeconds(15), true, false, null)],
        new(1.5, .3, 1.31, .11, 900, 900));
    public GridSnapshot Snapshot() => State;
}
