using global::Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;
using Domain.Process.Devices;

namespace UnitTests.Security.Grid.Support;

/// <summary>
/// A scripted, deterministic fake of the grid simulation for security tests. It keeps the mutable world state (switches,
/// taps, capacitor steps, sheds, faults, RTU online, spoofs, protection overrides) and recomputes a physically plausible
/// snapshot on every <see cref="Step"/>: energization from the sources, downstream flows, voltages (tap and capacitor
/// effects included), loading, measurements with 0.2 % noise, protection trips on injected faults and alarms. Training
/// and control fakes mutate it through its methods and queue <see cref="GridStepEvent"/>s that the next step reports.
/// </summary>
public sealed class SecurityFakeGridView : IGridView
{
    public const double NoisePercent = 0.2;
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, bool> _switchClosed;
    private readonly Dictionary<string, string?> _switchOperator;
    private readonly Dictionary<string, GridChangeCause> _switchCause;
    private readonly Dictionary<string, int> _tap;
    private readonly Dictionary<string, bool> _tapAutomatic;
    private readonly Dictionary<string, int> _capacitorSteps;
    private readonly HashSet<string> _shed;
    private readonly Dictionary<string, GridEquipmentFault> _faults;
    private readonly HashSet<string> _handledFaults;
    private readonly Dictionary<string, bool> _rtuOnline;
    private readonly Dictionary<string, double> _spoofs;
    private readonly Dictionary<string, (double PickupValue, double TimeSetting, bool Enabled)> _protectionOverrides;
    private readonly List<GridStepEvent> _pending = [];
    private readonly HashSet<string> _activeAlarms = new(StringComparer.Ordinal);
    private readonly Random _random;
    private GridSnapshot _snapshot;
    private DateTimeOffset _now = Start;
    private long _stepCount;

    public SecurityFakeGridView(GridDefinition definition, double stepSeconds = 1, int seed = 42)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        StepSeconds = stepSeconds > 0 ? stepSeconds : 1;
        _random = new Random(seed);
        _switchClosed = definition.Switches.ToDictionary(s => s.Id, s => s.NormallyClosed, StringComparer.Ordinal);
        _switchOperator = definition.Switches.ToDictionary(s => s.Id, _ => (string?)null, StringComparer.Ordinal);
        _switchCause = definition.Switches.ToDictionary(s => s.Id, _ => GridChangeCause.System, StringComparer.Ordinal);
        _tap = definition.Transformers.ToDictionary(t => t.Id, t => t.InitialTap, StringComparer.Ordinal);
        _tapAutomatic = definition.Transformers.ToDictionary(t => t.Id, t => t.AvrTargetLvKv is not null, StringComparer.Ordinal);
        _capacitorSteps = definition.Capacitors.ToDictionary(c => c.Id, c => c.InitialSteps, StringComparer.Ordinal);
        _shed = [];
        _faults = definition.Lines.Select(l => l.Id).Concat(definition.Transformers.Select(t => t.Id))
            .ToDictionary(id => id, _ => GridEquipmentFault.None, StringComparer.Ordinal);
        _handledFaults = [];
        _rtuOnline = definition.Rtus.ToDictionary(r => r.Id, _ => true, StringComparer.Ordinal);
        _spoofs = [];
        _protectionOverrides = [];
        _snapshot = Recompute();
    }

    public GridDefinition Definition { get; }
    public double StepSeconds { get; }
    public DateTimeOffset Now => _now;

    public GridStatus Status => new(false, _snapshot.SimulationTime, _stepCount, StepSeconds, 1.0, Definition.Name, 1);

    public GridSnapshot Snapshot() => _snapshot;

    /// <summary>Queues an event that the next <see cref="Step"/> reports.</summary>
    public int Emit(GridStepEvent stepEvent)
    {
        _pending.Add(stepEvent);
        return _pending.Count;
    }

    public int SetSwitchState(string switchId, bool closed, GridChangeCause cause, string? actor)
    {
        _switchClosed[switchId] = closed;
        _switchCause[switchId] = cause;
        _switchOperator[switchId] = actor;
        return closed ? 1 : 0;
    }

    public int SetTap(string transformerId, int tap, bool automatic)
    {
        _tap[transformerId] = tap;
        _tapAutomatic[transformerId] = automatic;
        return tap;
    }

    public int SetCapacitorSteps(string capacitorId, int steps)
    {
        _capacitorSteps[capacitorId] = steps;
        return steps;
    }

    public int SetShed(string loadId, bool shed)
    {
        return shed ? _shed.Add(loadId) ? 1 : 0 : _shed.Remove(loadId) ? 0 : -1;
    }

    public int SetFault(string elementId, GridEquipmentFault fault)
    {
        _faults[elementId] = fault;
        return (int)fault;
    }

    public int SetRtuOnline(string rtuId, bool online)
    {
        _rtuOnline[rtuId] = online;
        return online ? 1 : 0;
    }

    public int SetSpoof(string measurementId, double? value)
    {
        if (value is null)
            return _spoofs.Remove(measurementId) ? 0 : -1;
        _spoofs[measurementId] = value.Value;
        return 1;
    }

    public int SetProtection(string protectionId, double pickup, double time, bool enabled)
    {
        _protectionOverrides[protectionId] = (pickup, time, enabled);
        return enabled ? 1 : 0;
    }

    public int ClearProtectionOverride(string protectionId) => _protectionOverrides.Remove(protectionId) ? 1 : 0;

    public bool IsProtectionTampered(string protectionId) => _protectionOverrides.ContainsKey(protectionId);

    public int RestoreDefaults()
    {
        var restored = _faults.Count(f => f.Value != GridEquipmentFault.None) + _spoofs.Count + _protectionOverrides.Count
            + _rtuOnline.Count(r => !r.Value);
        foreach (var id in _faults.Keys.ToArray())
            _faults[id] = GridEquipmentFault.None;
        _handledFaults.Clear();
        foreach (var id in _rtuOnline.Keys.ToArray())
            _rtuOnline[id] = true;
        _spoofs.Clear();
        _protectionOverrides.Clear();
        return restored;
    }

    /// <summary>Advances the world by <paramref name="seconds"/> simulated seconds and returns the step report.</summary>
    public GridStepReport Step(double seconds, params GridStepEvent[] events)
    {
        _now = _now.AddSeconds(seconds);
        _stepCount++;
        var generated = new List<GridStepEvent>();
        HandleFaults(generated);
        HandleAvr(generated);
        _snapshot = Recompute();
        _snapshot = _snapshot with { SimulationTime = TimeSpan.FromSeconds(_snapshot.SimulationTime.TotalSeconds + seconds), StepCount = _stepCount };
        ApplyMeasurementNoise();
        UpdateAlarms(generated);
        var all = new List<GridStepEvent>(_pending.Count + generated.Count + events.Length);
        all.AddRange(_pending);
        all.AddRange(generated);
        all.AddRange(events);
        _pending.Clear();
        return new GridStepReport(_now, _snapshot, all);
    }

    private int HandleFaults(List<GridStepEvent> generated)
    {
        foreach (var (elementId, fault) in _faults)
        {
            if (fault == GridEquipmentFault.None || _handledFaults.Contains(elementId))
                continue;
            _handledFaults.Add(elementId);
            generated.Add(new GridStepEvent(GridStepEventKind.FaultInjected, elementId, $"Fault injected on {elementId}: {fault}", null, GridChangeCause.Unattributed));

            var guarding = Definition.Protections.Where(p => SwitchOf(p.SwitchId)?.ElementId == elementId).ToArray();
            var effective = guarding.FirstOrDefault(p => !_protectionOverrides.TryGetValue(p.Id, out var o) || (o.Enabled && o.PickupValue < 10_000));
            if (effective is not null)
            {
                Trip(effective, generated);
            }
            else
            {
                // Tampered/disabled relay: backup protection trips several breakers (cascade).
                var backup = Definition.Switches
                    .Where(s => s.Kind == GridSwitchKind.Breaker)
                    .OrderByDescending(s => s.ElementId == elementId)
                    .ThenBy(s => s.Id, StringComparer.Ordinal)
                    .Take(3)
                    .ToArray();
                foreach (var protection in guarding)
                    generated.Add(new GridStepEvent(GridStepEventKind.ProtectionPickup, protection.Id,
                        $"{protection.Name} picked up but is disabled/tampered", null, GridChangeCause.Protection));
                foreach (var switchDef in backup)
                {
                    var relay = Definition.Protections.FirstOrDefault(p => p.SwitchId == switchDef.Id);
                    SetSwitchState(switchDef.Id, false, GridChangeCause.Protection, null);
                    generated.Add(new GridStepEvent(GridStepEventKind.ProtectionTripped, relay?.Id ?? switchDef.Id,
                        $"Backup protection tripped {switchDef.Id}", null, GridChangeCause.Protection));
                    generated.Add(new GridStepEvent(GridStepEventKind.SwitchOperated, switchDef.Id,
                        $"{switchDef.Name} opened by protection", null, GridChangeCause.Protection));
                }
            }
        }

        return 0;
    }

    private int Trip(GridProtectionDefinition protection, List<GridStepEvent> generated)
    {
        SetSwitchState(protection.SwitchId, false, GridChangeCause.Protection, null);
        generated.Add(new GridStepEvent(GridStepEventKind.ProtectionPickup, protection.Id, $"{protection.Name} picked up", null, GridChangeCause.Protection));
        generated.Add(new GridStepEvent(GridStepEventKind.ProtectionTripped, protection.Id, $"{protection.Name} tripped {protection.SwitchId}", null, GridChangeCause.Protection));
        generated.Add(new GridStepEvent(GridStepEventKind.SwitchOperated, protection.SwitchId,
            $"{SwitchOf(protection.SwitchId)?.Name} opened by protection", null, GridChangeCause.Protection));

        return 0;
    }

    private int HandleAvr(List<GridStepEvent> generated)
    {
        // The AVR only reacts outside a wide band, so normal operation never changes taps.
        foreach (var transformer in Definition.Transformers.Where(t => t.HasOnLoadTapChanger && t.AvrTargetLvKv is not null))
        {
            if (!_tapAutomatic[transformer.Id])
                continue;
            var lv = _snapshot.Bus(transformer.LvBusId);
            if (lv is null || !lv.Energized)
                continue;
            var target = transformer.AvrTargetLvKv!.Value;
            if (lv.VoltageKv > target * 1.05 && _tap[transformer.Id] > transformer.TapMin)
            {
                SetTap(transformer.Id, _tap[transformer.Id] - 1, automatic: true);
                generated.Add(new GridStepEvent(GridStepEventKind.TapChanged, transformer.Id, $"AVR lowered tap to {_tap[transformer.Id]}", null, GridChangeCause.Automatic));
            }
            else if (lv.VoltageKv < target * 0.95 && _tap[transformer.Id] < transformer.TapMax)
            {
                SetTap(transformer.Id, _tap[transformer.Id] + 1, automatic: true);
                generated.Add(new GridStepEvent(GridStepEventKind.TapChanged, transformer.Id, $"AVR raised tap to {_tap[transformer.Id]}", null, GridChangeCause.Automatic));
            }
        }

        return 0;
    }

    private GridSwitchDefinition? SwitchOf(string switchId) => Definition.Switches.FirstOrDefault(s => s.Id == switchId);

    private double LoadFactor()
    {
        // Deterministic daily residential-ish profile: light at night, peak in the evening.
        var hour = _now.TimeOfDay.TotalHours;
        return 0.55 + 0.45 * Math.Sin(Math.PI * (hour - 6) / 24 * 2 - Math.PI / 2) * 0.5 + 0.225;
    }

    private GridSnapshot Recompute()
    {
        var buses = Definition.Buses.Select(b => (Def: b, Energized: false, Pu: 1.0)).ToDictionary(b => b.Def.Id, b => b, StringComparer.Ordinal);
        foreach (var source in Definition.Sources)
            if (buses.TryGetValue(source.BusId, out var sb))
                buses[source.BusId] = (sb.Def, true, source.VoltageSetpointPu);

        // Energize: BFS over branches whose from-side switch is closed.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var line in Definition.Lines)
            {
                var from = buses[line.FromBusId];
                var to = buses[line.ToBusId];
                var sw = SwitchOfLineAt(line.Id, line.FromBusId);
                if (from.Energized && (sw is null || _switchClosed[sw.Id]) && !to.Energized)
                {
                    buses[line.ToBusId] = (to.Def, true, from.Pu);
                    changed = true;
                }
            }
            foreach (var transformer in Definition.Transformers)
            {
                var hv = buses[transformer.HvBusId];
                var lv = buses[transformer.LvBusId];
                var sw = SwitchOfLineAt(transformer.Id, transformer.HvBusId);
                if (hv.Energized && (sw is null || _switchClosed[sw.Id]) && !lv.Energized)
                {
                    var boost = 1 + (_tap[transformer.Id] - transformer.TapNeutral) * transformer.TapStepPercent / 100.0;
                    buses[transformer.LvBusId] = (lv.Def, true, hv.Pu * boost);
                    changed = true;
                }
            }
        }

        // Downstream load per branch (used for flows and loading).
        var loadMw = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var load in Definition.Loads)
            loadMw[load.Id] = 0;
        var loadAtBus = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var load in Definition.Loads)
        {
            var bus = buses[load.BusId];
            var energized = bus.Energized && !_shed.Contains(load.Id);
            loadAtBus[load.BusId] = loadAtBus.GetValueOrDefault(load.BusId) + (energized ? load.PeakActivePowerMw * LoadFactor() : 0);
            loadMw[load.Id] = energized ? load.PeakActivePowerMw * LoadFactor() : 0;
        }
        var hour0 = _now.TimeOfDay.TotalHours;
        var sun0 = Math.Clamp(Math.Sin(Math.PI * (hour0 - 6) / 12), 0, 1);
        foreach (var generator in Definition.Generators)
        {
            if (!buses[generator.BusId].Energized)
                continue;
            var output = generator.Kind == GridGeneratorKind.Photovoltaic ? generator.RatedPowerMw * sun0 : generator.RatedPowerMw * 0.8;
            loadAtBus[generator.BusId] = loadAtBus.GetValueOrDefault(generator.BusId) - output; // net injection
        }

        double DownstreamOf(string branchId, string fromBus, string toBus)
        {
            // Sum loads reachable from toBus without crossing the branch itself.
            var seen = new HashSet<string>(StringComparer.Ordinal) { fromBus };
            var stack = new Stack<string>();
            stack.Push(toBus);
            var total = 0.0;
            while (stack.Count > 0)
            {
                var bus = stack.Pop();
                if (!seen.Add(bus))
                    continue;
                total += loadAtBus.GetValueOrDefault(bus);
                foreach (var line in Definition.Lines.Where(l => l.Id != branchId))
                {
                    if (line.FromBusId == bus && !seen.Contains(line.ToBusId))
                        stack.Push(line.ToBusId);
                    if (line.ToBusId == bus && !seen.Contains(line.FromBusId))
                        stack.Push(line.FromBusId);
                }
                foreach (var transformer in Definition.Transformers.Where(t => t.Id != branchId))
                {
                    if (transformer.HvBusId == bus && !seen.Contains(transformer.LvBusId))
                        stack.Push(transformer.LvBusId);
                    if (transformer.LvBusId == bus && !seen.Contains(transformer.HvBusId))
                        stack.Push(transformer.HvBusId);
                }
            }
            return total;
        }

        var branches = new List<BranchSnapshot>();
        foreach (var line in Definition.Lines)
        {
            var flow = DownstreamOf(line.Id, line.FromBusId, line.ToBusId);
            var kv = buses[line.FromBusId].Def.NominalVoltageKv;
            var current = flow * 1000.0 / (Math.Sqrt(3) * kv);
            var energized = buses[line.FromBusId].Energized && buses[line.ToBusId].Energized
                && (SwitchOfLineAt(line.Id, line.FromBusId) is { } sw ? _switchClosed[sw.Id] : true);
            if (!energized)
                flow = 0;
            var losses = flow * 0.01;
            branches.Add(new BranchSnapshot(line.Id, line.Name, GridBranchKind.Line, line.FromBusId, line.ToBusId, energized,
                current, flow, flow * 0.1, flow - losses, flow * 0.1 - losses, losses,
                line.RatedCurrentA > 0 ? current / line.RatedCurrentA * 100.0 : 0, null, null, _faults.GetValueOrDefault(line.Id)));
        }
        foreach (var transformer in Definition.Transformers)
        {
            var flow = DownstreamOf(transformer.Id, transformer.HvBusId, transformer.LvBusId);
            var kv = buses[transformer.HvBusId].Def.NominalVoltageKv;
            var current = flow * 1000.0 / (Math.Sqrt(3) * kv);
            var ratedA = transformer.RatedPowerMva * 1000.0 / (Math.Sqrt(3) * transformer.HvRatedKv);
            var energized = buses[transformer.HvBusId].Energized && buses[transformer.LvBusId].Energized
                && (SwitchOfLineAt(transformer.Id, transformer.HvBusId) is { } sw ? _switchClosed[sw.Id] : true);
            if (!energized)
                flow = 0;
            branches.Add(new BranchSnapshot(transformer.Id, transformer.Name, GridBranchKind.Transformer, transformer.HvBusId, transformer.LvBusId,
                energized, current, flow, flow * 0.1, flow * 0.99, flow * 0.1, flow * 0.01,
                ratedA > 0 ? current / ratedA * 100.0 : 0, _tap[transformer.Id], _tapAutomatic[transformer.Id],
                _faults.GetValueOrDefault(transformer.Id)));
        }

        // Voltages: line drop, then capacitor boost and load drop per bus.
        foreach (var line in Definition.Lines)
        {
            if (!buses[line.ToBusId].Energized)
                continue;
            var from = buses[line.FromBusId];
            var to = buses[line.ToBusId];
            var flow = branches.First(b => b.Id == line.Id).ActivePowerFromMw;
            buses[line.ToBusId] = (to.Def, true, Math.Max(0.5, from.Pu - flow * 0.0008));
        }
        var busSnapshots = Definition.Buses.Select(b =>
        {
            var state = buses[b.Id];
            var pu = state.Pu;
            if (state.Energized)
            {
                var capBoost = Definition.Capacitors.Where(c => c.BusId == b.Id).Sum(c => _capacitorSteps[c.Id] * 0.03);
                var drop = loadAtBus.GetValueOrDefault(b.Id) * 0.004;
                pu = pu + capBoost - drop;
            }
            return new BusSnapshot(b.Id, b.Name, b.NominalVoltageKv, state.Energized, pu * b.NominalVoltageKv, pu,
                state.Energized ? 0 : -999);
        }).ToArray();

        var switches = Definition.Switches.Select(s => new SwitchSnapshot(s.Id, s.Name, s.Kind, s.BusId, s.ElementId,
            _switchClosed[s.Id], Selected: false, SelectedToClose: null, _switchOperator[s.Id], _switchCause[s.Id],
            GridEquipmentFault.None)).ToArray();

        var loads = Definition.Loads.Select(l =>
        {
            var bus = buses[l.BusId];
            var energized = bus.Energized && !_shed.Contains(l.Id);
            var mw = loadMw[l.Id];
            return new LoadSnapshot(l.Id, l.Name, l.BusId, energized, l.PeakActivePowerMw * LoadFactor(), mw, mw * 0.3,
                l.Customers, energized ? l.Customers : 0, _shed.Contains(l.Id), l.Critical);
        }).ToArray();

        var hour = _now.TimeOfDay.TotalHours;
        var sun = Math.Clamp(Math.Sin(Math.PI * (hour - 6) / 12), 0, 1);
        var generators = Definition.Generators.Select(g => new GeneratorSnapshot(g.Id, g.Name, g.BusId, g.Kind,
            buses[g.BusId].Energized, g.Kind == GridGeneratorKind.Photovoltaic ? g.RatedPowerMw * sun : g.RatedPowerMw * 0.8,
            0)).ToArray();

        var capacitors = Definition.Capacitors.Select(c => new CapacitorSnapshot(c.Id, c.Name, c.BusId, _capacitorSteps[c.Id],
            c.Steps, _capacitorSteps[c.Id] * c.RatedPowerMvar / Math.Max(1, c.Steps))).ToArray();

        var protections = Definition.Protections.Select(p =>
        {
            var o = _protectionOverrides.TryGetValue(p.Id, out var ov) ? ov : (p.PickupValue, p.TimeSetting, p.Enabled);
            return new ProtectionSnapshot(p.Id, p.Name, p.SwitchId, p.Function, p.Curve, o.PickupValue, o.TimeSetting, o.Enabled,
                PickedUp: false, Tripped: false, null, _protectionOverrides.ContainsKey(p.Id));
        }).ToArray();

        var measurements = BuildMeasurements(buses, busSnapshots, branches, switches).ToArray();

        var totalLoad = loads.Sum(l => l.ActivePowerMw);
        var generation = generators.Sum(g => g.ActivePowerMw);
        var totals = new GridTotals(totalLoad, generation, Math.Max(0, totalLoad - generation + branches.Sum(b => b.LossesMw)),
            branches.Sum(b => b.LossesMw), loads.Sum(l => l.Customers), loads.Sum(l => l.CustomersSupplied));

        return new GridSnapshot(Definition.Name, _stepCount, _snapshot?.SimulationTime ?? TimeSpan.Zero, _stepCount, 50.0, true, 3,
            busSnapshots, branches, switches, loads, generators, capacitors, protections, measurements, [], totals);
    }

    private GridSwitchDefinition? SwitchOfLineAt(string elementId, string busId) =>
        Definition.Switches.FirstOrDefault(s => s.ElementId == elementId && s.BusId == busId);

    private IEnumerable<GridMeasurementSnapshot> BuildMeasurements(
        Dictionary<string, (GridBusDefinition Def, bool Energized, double Pu)> buses,
        BusSnapshot[] busSnapshots,
        List<BranchSnapshot> branches,
        SwitchSnapshot[] switches)
    {
        foreach (var bus in busSnapshots)
        {
            var rtu = RtuForBus(bus.Id);
            yield return Measurement($"M-{bus.Id}-V", bus.Id, rtu, GridMeasurementKind.Voltage, "kV", bus.VoltageKv);
        }
        foreach (var branch in branches)
        {
            var rtu = RtuForElement(branch.Id);
            yield return Measurement($"M-{branch.Id}-I", branch.Id, rtu, GridMeasurementKind.Current, "A", branch.CurrentA);
            yield return Measurement($"M-{branch.Id}-P", branch.Id, rtu, GridMeasurementKind.ActivePower, "MW", branch.ActivePowerFromMw);
            if (branch.Kind == GridBranchKind.Transformer && branch.TapPosition is { } tap)
                yield return Measurement($"M-{branch.Id}-TAP", branch.Id, rtu, GridMeasurementKind.TapPosition, "tap", tap);
        }
        foreach (var switchSnapshot in switches)
        {
            var rtu = RtuForElement(switchSnapshot.Id);
            yield return Measurement($"M-{switchSnapshot.Id}-S", switchSnapshot.Id, rtu, GridMeasurementKind.SwitchState, "",
                switchSnapshot.Closed ? 1 : 0);
        }
    }

    private string RtuForElement(string elementId) =>
        Definition.Rtus.FirstOrDefault(r => r.ElementIds.Contains(elementId))?.Id ?? Definition.Rtus[0].Id;

    private string RtuForBus(string busId)
    {
        var feeder = Definition.Lines.FirstOrDefault(l => l.ToBusId == busId) ?? Definition.Lines.FirstOrDefault(l => l.FromBusId == busId);
        if (feeder is not null && Definition.Rtus.FirstOrDefault(r => r.ElementIds.Contains(feeder.Id)) is { } rtu)
            return rtu.Id;
        var transformer = Definition.Transformers.FirstOrDefault(t => t.LvBusId == busId || t.HvBusId == busId);
        if (transformer is not null && Definition.Rtus.FirstOrDefault(r => r.ElementIds.Contains(transformer.Id)) is { } rtu2)
            return rtu2.Id;
        return Definition.Rtus[0].Id;
    }

    private GridMeasurementSnapshot Measurement(string id, string elementId, string rtuId, GridMeasurementKind kind, string unit, double trueValue)
    {
        var quality = _rtuOnline.GetValueOrDefault(rtuId, true) ? MeasurementQuality.Good : MeasurementQuality.Bad;
        var spoofed = _spoofs.TryGetValue(id, out var value);
        var reported = spoofed ? value : trueValue;
        return new GridMeasurementSnapshot(id, elementId, rtuId, kind, unit, reported, quality, trueValue, spoofed, spoofed ? value : null);
    }

    private int ApplyMeasurementNoise()
    {
        // 0.2 % telemetry noise on every good, unspoofed measurement.
        _snapshot = _snapshot with
        {
            Measurements = _snapshot.Measurements.Select(m =>
            {
                if (m.IsSpoofed || m.Quality != MeasurementQuality.Good)
                    return m;
                var noise = (_random.NextDouble() * 2 - 1) * NoisePercent / 100.0;
                return m with { ReportedValue = m.TrueValue * (1 + noise) };
            }).ToArray()
        };

        return 0;
    }

    private int UpdateAlarms(List<GridStepEvent> generated)
    {
        var alarms = new List<GridAlarm>();
        var now = _snapshot.SimulationTime;
        foreach (var bus in _snapshot.Buses.Where(b => b.Energized))
        {
            var def = Definition.Buses.First(b => b.Id == bus.Id);
            if (bus.VoltagePu > def.MaxVoltagePu)
                alarms.Add(Alarm($"{bus.Id}:over", bus.Id, GridAlarmType.Overvoltage, $"{bus.Name} overvoltage {bus.VoltagePu:0.###} pu"));
            else if (bus.VoltagePu < def.MinVoltagePu)
                alarms.Add(Alarm($"{bus.Id}:under", bus.Id, GridAlarmType.Undervoltage, $"{bus.Name} undervoltage {bus.VoltagePu:0.###} pu"));
        }
        foreach (var branch in _snapshot.Branches.Where(b => b.Energized && b.LoadingPercent > 100))
            alarms.Add(Alarm($"{branch.Id}:overload", branch.Id, GridAlarmType.Overload, $"{branch.Name} overload {branch.LoadingPercent:0.#}%"));
        foreach (var rtu in Definition.Rtus.Where(r => !_rtuOnline[r.Id]))
            alarms.Add(Alarm($"{rtu.Id}:comms", rtu.Id, GridAlarmType.CommunicationLoss, $"RTU {rtu.Name} offline"));

        foreach (var alarm in alarms.Where(a => !_activeAlarms.Contains(a.Key)))
            generated.Add(new GridStepEvent(GridStepEventKind.AlarmRaised, alarm.SourceId, alarm.Message, null, GridChangeCause.System));
        foreach (var key in _activeAlarms.Where(k => alarms.All(a => a.Key != k)).ToArray())
            generated.Add(new GridStepEvent(GridStepEventKind.AlarmCleared, key.Split(':')[0], $"Alarm {key} cleared", null, GridChangeCause.System));

        _activeAlarms.Clear();
        foreach (var alarm in alarms)
            _activeAlarms.Add(alarm.Key);
        _snapshot = _snapshot with { Alarms = alarms };

        GridAlarm Alarm(string key, string source, GridAlarmType type, string message) =>
            new(key, source, type, global::Domain.Process.Alarms.AlarmSeverity.High, message, now, Active: true, Acknowledged: false, null);

        return 0;
    }
}
