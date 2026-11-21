using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;
using Domain.Process.Devices;

namespace IntegrationTests.Support;

/// <summary>
/// Minimal scripted <see cref="IGridView"/> for API-level grid security tests: one 10 kV source bus, one feeder line
/// with a breaker, one load, one protection relay and one RTU. <see cref="Step"/> returns a report with the queued events.
/// </summary>
public sealed class SecurityFakeGridView : IGridView
{
    public const string BusSource = "B1";
    public const string BusFeeder = "B2";
    public const string Line = "L1";
    public const string Breaker = "CB1";
    public const string Load = "LD1";
    public const string Protection = "PR1";
    public const string Rtu = "RTU1";

    private readonly List<GridStepEvent> _pending = [];
    private GridSnapshot _snapshot;
    private DateTimeOffset _now = new(2026, 2, 2, 8, 0, 0, TimeSpan.Zero);
    private long _stepCount;
    private bool _breakerClosed = true;

    public SecurityFakeGridView()
    {
        Definition = BuildDefinition();
        _snapshot = BuildSnapshot();
    }

    public GridDefinition Definition { get; }

    public GridStatus Status => new(false, _snapshot.SimulationTime, _stepCount, 1, 1.0, Definition.Name, 1);

    public GridSnapshot Snapshot() => _snapshot;

    public int Emit(GridStepEvent stepEvent)
    {
        _pending.Add(stepEvent);
        return _pending.Count;
    }

    public int SetBreakerClosed(bool closed)
    {
        _breakerClosed = closed;
        _snapshot = BuildSnapshot();
        return closed ? 1 : 0;
    }

    public GridStepReport Step(double seconds = 1, params GridStepEvent[] events)
    {
        _now = _now.AddSeconds(seconds);
        _stepCount++;
        var all = new List<GridStepEvent>(_pending.Count + events.Length);
        all.AddRange(_pending);
        all.AddRange(events);
        _pending.Clear();
        _snapshot = _snapshot with
        {
            SimulationTime = _snapshot.SimulationTime.Add(TimeSpan.FromSeconds(seconds)),
            StepCount = _stepCount
        };
        return new GridStepReport(_now, _snapshot, all);
    }

    private GridDefinition BuildDefinition() => new(
        "API test feeder", 50.0,
        Buses:
        [
            new GridBusDefinition(BusSource, "Source bus", 10, GridBusKind.Busbar, new DiagramPosition(0, 0)),
            new GridBusDefinition(BusFeeder, "Feeder bus", 10, GridBusKind.ConsumerPoint, new DiagramPosition(4, 0))
        ],
        Sources: [new GridSourceDefinition("SRC", "Upstream", BusSource, 1.0, 500, new DiagramPosition(0, 1))],
        Lines: [new GridLineDefinition(Line, "Feeder line", BusSource, BusFeeder, GridLineKind.Cable, 2, 0.1, 0.08, 3, 300, null, [])],
        Transformers: [],
        Switches: [new GridSwitchDefinition(Breaker, "Feeder breaker", GridSwitchKind.Breaker, BusSource, Line, true, 25, new DiagramPosition(2, 0))],
        Loads: [new GridLoadDefinition(Load, "Feeder load", BusFeeder, 1.0, 0.95, GridLoadProfile.Residential, 200, false, new DiagramPosition(4, 1))],
        Generators: [],
        Capacitors: [],
        Protections: [new GridProtectionDefinition(Protection, "Feeder relay", Breaker, GridProtectionFunction.Overcurrent51, GridCurveType.IecStandardInverse, 300, 0.3, true)],
        Rtus: [new GridRtuDefinition(Rtu, "Feeder RTU", 1, "LINK-1", [Line, Breaker, BusSource, BusFeeder, Load])]);

    private GridSnapshot BuildSnapshot()
    {
        var energized = _breakerClosed;
        var buses = new[]
        {
            new BusSnapshot(BusSource, "Source bus", 10, true, 10.0, 1.0, 0),
            new BusSnapshot(BusFeeder, "Feeder bus", 10, energized, energized ? 9.9 : 0, energized ? 0.99 : 0, energized ? 0 : -999)
        };
        var branches = new[]
        {
            new BranchSnapshot(Line, "Feeder line", GridBranchKind.Line, BusSource, BusFeeder, energized, energized ? 58.0 : 0,
                energized ? 1.0 : 0, 0.1, energized ? 0.99 : 0, 0.1, 0.01, energized ? 19.3 : 0, null, null, GridEquipmentFault.None)
        };
        var switches = new[]
        {
            new SwitchSnapshot(Breaker, "Feeder breaker", GridSwitchKind.Breaker, BusSource, Line, _breakerClosed, false, null, null, GridChangeCause.System, GridEquipmentFault.None)
        };
        var loads = new[]
        {
            new LoadSnapshot(Load, "Feeder load", BusFeeder, energized, 1.0, energized ? 1.0 : 0, 0.3, 200, energized ? 200 : 0, false, false)
        };
        var measurements = new[]
        {
            new GridMeasurementSnapshot($"M-{BusSource}-V", BusSource, Rtu, GridMeasurementKind.Voltage, "kV", 10.0, MeasurementQuality.Good, 10.0, false, null),
            new GridMeasurementSnapshot($"M-{BusFeeder}-V", BusFeeder, Rtu, GridMeasurementKind.Voltage, "kV", energized ? 9.9 : 0, MeasurementQuality.Good, energized ? 9.9 : 0, false, null),
            new GridMeasurementSnapshot($"M-{Line}-I", Line, Rtu, GridMeasurementKind.Current, "A", energized ? 58.0 : 0, MeasurementQuality.Good, energized ? 58.0 : 0, false, null),
            new GridMeasurementSnapshot($"M-{Line}-P", Line, Rtu, GridMeasurementKind.ActivePower, "MW", energized ? 1.0 : 0, MeasurementQuality.Good, energized ? 1.0 : 0, false, null),
            new GridMeasurementSnapshot($"M-{Breaker}-S", Breaker, Rtu, GridMeasurementKind.SwitchState, "", _breakerClosed ? 1 : 0, MeasurementQuality.Good, _breakerClosed ? 1 : 0, false, null)
        };
        var protections = new[]
        {
            new ProtectionSnapshot(Protection, "Feeder relay", Breaker, GridProtectionFunction.Overcurrent51, GridCurveType.IecStandardInverse, 300, 0.3, true, false, false, null, false)
        };
        var totals = new GridTotals(energized ? 1.0 : 0, 0, energized ? 1.01 : 0, 0.01, 200, energized ? 200 : 0);
        return new GridSnapshot(Definition.Name, _stepCount, _snapshot?.SimulationTime ?? TimeSpan.Zero, _stepCount, 50.0, true, 3,
            buses, branches, switches, loads, [], [], protections, measurements, [], totals);
    }
}
