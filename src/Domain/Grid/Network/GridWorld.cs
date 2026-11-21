using System.Numerics;
using Domain.Grid.Commands;
using Domain.Grid.Definition;
using Domain.Grid.PowerFlow;
using Domain.Grid.Profiles;
using Domain.Grid.Protection;
using Domain.Grid.Snapshots;
using Domain.Process.Alarms;
using Domain.Process.Devices;

namespace Domain.Grid.Network;

/// <summary>Mutable electrical world. Its owner must serialize access.</summary>
public sealed class GridWorld
{
    private readonly Random _random;
    private readonly GridPowerFlowSolver _solver = new();
    private readonly Dictionary<string, double> _avrTime = [];
    private readonly Dictionary<string, int> _avrDirection = [];
    private readonly Dictionary<string, double> _alarmTime = [];
    private readonly Dictionary<string, GridAlarm> _alarms = [];
    private readonly Dictionary<string, GridMeasurementSnapshot> _measurements = [];
    private readonly List<GridTransition> _transitions = [];
    private GridPowerFlowResult _flow;
    private bool _converged;
    private int _iterations;
    private double _frequency;

    public GridWorld(GridDefinition definition, int seed, TimeSpan startTime)
    {
        Definition = definition;
        _random = new(seed);
        SimulationTime = startTime;
        Switches = definition.Switches.ToDictionary(s => s.Id, s => new GridSwitchState(s.NormallyClosed));
        Taps = definition.Transformers.ToDictionary(t => t.Id, t => t.InitialTap);
        TapAutomatic = definition.Transformers.ToDictionary(t => t.Id, t => t.HasOnLoadTapChanger && t.AvrTargetLvKv.HasValue);
        CapacitorSteps = definition.Capacitors.ToDictionary(c => c.Id, c => c.InitialSteps);
        Relays = definition.Protections.ToDictionary(p => p.Id, p => new GridRelayState(p));
        RtuOnline = definition.Rtus.ToDictionary(r => r.Id, _ => true);
        UpdateDemand();
        _flow = _solver.Solve(this);
        _converged = _flow.Converged; _iterations = _flow.Iterations;
        _frequency = definition.NominalFrequencyHz;
        RefreshMeasurements();
    }

    public GridDefinition Definition { get; }
    public TimeSpan SimulationTime { get; private set; }
    public long StepCount { get; private set; }
    public long Revision { get; private set; }
    public Dictionary<string, GridSwitchState> Switches { get; }
    public Dictionary<string, int> Taps { get; }
    public Dictionary<string, bool> TapAutomatic { get; }
    public Dictionary<string, int> CapacitorSteps { get; }
    public Dictionary<string, GridRelayState> Relays { get; }
    public Dictionary<string, bool> RtuOnline { get; }
    public Dictionary<string, GridEquipmentFault> Faults { get; } = [];
    public Dictionary<string, double> Spoofs { get; } = [];
    public HashSet<string> ShedLoads { get; } = [];
    public Dictionary<string, double> Demand { get; } = [];
    public Dictionary<string, double> Generation { get; } = [];

    public bool Connected(string elementId) => Definition.Switches.Where(s => s.ElementId == elementId).All(s => Switches[s.Id].Closed);
    public bool TerminalConnected(string elementId, string busId) => Definition.Switches
        .Where(s => s.ElementId == elementId && s.BusId == busId).All(s => Switches[s.Id].Closed);
    public bool Reachable(string elementId) => Definition.Rtus.Where(r => r.ElementIds.Contains(elementId)).All(r => RtuOnline[r.Id]);

    public IReadOnlyList<GridTransition> DrainEvents()
    {
        var events = _transitions.ToArray(); _transitions.Clear(); return events;
    }

    public GridSnapshot Step(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        // Fixed simulation-second profile samples and exact relay/AVR crossings are independent of outer ticks.
        var end = SimulationTime + TimeSpan.FromSeconds(seconds);
        var remaining = seconds;
        while (remaining > 1e-8)
        {
            var untilProfile = 1 - (SimulationTime.Ticks % TimeSpan.TicksPerSecond) / (double)TimeSpan.TicksPerSecond;
            var dt = Math.Min(remaining, untilProfile);
            if (_converged)
            {
                foreach (var relay in Relays.Values)
                {
                    var value = ProtectionMultiple(relay.Settings);
                    if (value <= 1 || !relay.Settings.Enabled || relay.Failed) continue;
                    var duration = GridProtectionCurves.OperateSeconds(relay.Settings.Curve, value, relay.Settings.TimeSetting);
                    dt = Math.Min(dt, Math.Max(1e-6, duration * Math.Max(0, 1 - relay.Progress)));
                }
                foreach (var t in Definition.Transformers.Where(t => TapAutomatic[t.Id] && t.AvrTargetLvKv.HasValue))
                {
                    var direction = AvrDirection(t);
                    if (direction == 0) continue;
                    var elapsed = direction == _avrDirection.GetValueOrDefault(t.Id) ? _avrTime.GetValueOrDefault(t.Id) : 0;
                    dt = Math.Min(dt, Math.Max(1e-6, t.AvrDelaySeconds - elapsed));
                }
            }
            var ticks = Math.Min((end - SimulationTime).Ticks, Math.Max(1, (long)Math.Ceiling(dt * TimeSpan.TicksPerSecond - 1e-6)));
            dt = ticks / (double)TimeSpan.TicksPerSecond;
            SimulationTime += TimeSpan.FromTicks(ticks);
            UpdateAlarms(dt);
            if (_converged)
            {
                // Integrate every timer against the same pre-event electrical state, then apply boundary events.
                RunProtection(dt, false);
                RunAvr(dt, false);
                RunProtection(0);
                RunAvr(0);
            }
            else
            {
                foreach (var relay in Relays.Values) { relay.Progress = 0; relay.PickedUp = false; }
                _avrTime.Clear(); _avrDirection.Clear();
            }
            remaining = (end - SimulationTime).TotalSeconds;
            if (SimulationTime.Ticks % TimeSpan.TicksPerSecond == 0) { UpdateDemand(); Solve(); }
        }
        SimulationTime = end;
        StepCount++; Revision++;
        UpdateAlarms(0);
        _frequency = Definition.NominalFrequencyHz + (_random.NextDouble() - 0.5) * 0.01;
        RefreshMeasurements();
        return Snapshot();
    }

    public GridCommandResult OperateSwitch(string id, bool closed, string? actor, GridChangeCause cause)
    {
        if (!Switches.TryGetValue(id, out var state)) return Reject(GridCommandRejection.UnknownTarget, "Unknown switch.");
        if (state.Closed == closed) return Reject(GridCommandRejection.AlreadyInState, "Switch already in requested state.");
        if (!closed && !_converged)
            return Reject(GridCommandRejection.Interlocked, "Current solution unavailable; cannot verify interruption capability.");
        var definition = Definition.Switches.First(s => s.Id == id);
        var current = CurrentFor(definition.ElementId, definition.BusId);
        var exceeded = definition.BreakingCapacityKa is { } capacity && current > capacity * 1000;
        var loadBreakFault = definition.Kind == GridSwitchKind.LoadBreakSwitch && _flow.FaultActive && current > 1
            && definition.BreakingCapacityKa is null;
        if (!closed && (Faults.GetValueOrDefault(id) == GridEquipmentFault.SwitchStuck || exceeded || loadBreakFault))
        {
            var reason = exceeded || loadBreakFault ? "Interrupting capability exceeded" : "Switch stuck";
            if (!Active(id, GridAlarmType.BreakerFailure))
            {
                Revision++;
                _transitions.Add(new("BreakerFailed", id, $"{reason}: {current:0.#} A; backup required", actor, cause));
            }
            SetAlarm(id, GridAlarmType.BreakerFailure, true, AlarmSeverity.High, reason, 0, 0);
            return Reject(GridCommandRejection.Interlocked, reason);
        }
        foreach (var relay in Relays.Values.Where(r => r.Settings.SwitchId == id))
        { relay.Progress = 0; relay.PickedUp = false; relay.Failed = false; }
        SetAlarm(id, GridAlarmType.BreakerFailure, false, AlarmSeverity.High, "Breaker opened", 0, 0);
        state.Operations++;
        state.Closed = closed; state.SelectedToClose = null; state.SelectedUntil = null; state.SelectedBy = null;
        state.LastOperatedBy = actor; state.LastCause = cause; Revision++;
        _transitions.Add(new("SwitchOperated", id, closed ? "Switch closed" : "Switch opened", actor, cause));
        Solve(); RefreshMeasurements();
        return GridCommandResult.Ok(closed ? "Closed." : "Opened.");
    }

    public GridCommandResult ApplyCommand(GridCommand command, string actor, DateTimeOffset now, TimeSpan selectTimeout)
    {
        var id = command.TargetId;
        if (!Enum.IsDefined(command.Type) || string.IsNullOrWhiteSpace(id) || command.Value is { } v && !double.IsFinite(v))
            return Reject(GridCommandRejection.InvalidValue, "Invalid command.");
        if (!Reachable(id)) return Reject(GridCommandRejection.CommunicationLoss, "Owning RTU is offline.");
        if (command.Type is GridCommandType.SelectOpen or GridCommandType.SelectClose or GridCommandType.Execute or GridCommandType.Cancel)
        {
            if (!Switches.TryGetValue(id, out var state)) return Reject(GridCommandRejection.UnknownTarget, "Unknown switch.");
            if (command.Type == GridCommandType.Cancel)
            {
                if (state.SelectedBy is not null && state.SelectedBy != actor) return Reject(GridCommandRejection.NotSelected, "Selected by another actor.");
                state.SelectedToClose = null; state.SelectedUntil = null; state.SelectedBy = null;
                return GridCommandResult.Ok("Selection cancelled.");
            }
            if (command.Type is GridCommandType.SelectOpen or GridCommandType.SelectClose)
            {
                if (state.SelectedBy is not null && state.SelectedBy != actor && state.SelectedUntil > now)
                    return Reject(GridCommandRejection.Interlocked, "Selected by another actor.");
                state.SelectedToClose = command.Type == GridCommandType.SelectClose;
                state.SelectedBy = actor; state.SelectedUntil = now + selectTimeout;
                return GridCommandResult.Ok("Selected.");
            }
            if (state.SelectedToClose is not { } close || state.SelectedBy != actor)
                return Reject(GridCommandRejection.NotSelected, "Select the switch first.");
            state.SelectedToClose = null; state.SelectedBy = null;
            if (state.SelectedUntil <= now) { state.SelectedUntil = null; return Reject(GridCommandRejection.SelectionExpired, "Selection expired."); }
            state.SelectedUntil = null;
            var sw = Definition.Switches.First(s => s.Id == id);
            if (sw.Kind == GridSwitchKind.Disconnector && CurrentFor(sw.ElementId, sw.BusId) > 1)
                return Reject(GridCommandRejection.Interlocked, "Disconnector cannot operate under load.");
            return OperateSwitch(id, close, actor, GridChangeCause.Operator);
        }
        if (command.Type is GridCommandType.TapRaise or GridCommandType.TapLower or GridCommandType.SetTapAutomatic or GridCommandType.SetTapManual)
        {
            var t = Definition.Transformers.FirstOrDefault(t => t.Id == id && t.HasOnLoadTapChanger);
            if (t is null) return Reject(GridCommandRejection.UnknownTarget, "Unknown OLTC.");
            if (command.Type is GridCommandType.SetTapAutomatic or GridCommandType.SetTapManual)
            { TapAutomatic[id] = command.Type == GridCommandType.SetTapAutomatic; _avrTime[id] = 0; _avrDirection[id] = 0; return GridCommandResult.Ok("Tap mode changed."); }
            if (TapAutomatic[id]) return Reject(GridCommandRejection.Interlocked, "Set tap to manual first.");
            return SetTap(id, Taps[id] + (command.Type == GridCommandType.TapRaise ? 1 : -1), actor, GridChangeCause.Operator);
        }
        if (command.Type == GridCommandType.SetCapacitorSteps)
        {
            var c = Definition.Capacitors.FirstOrDefault(c => c.Id == id);
            if (c is null) return Reject(GridCommandRejection.UnknownTarget, "Unknown capacitor.");
            if (command.Value is not { } steps || steps != Math.Truncate(steps) || steps < 0 || steps > c.Steps)
                return Reject(GridCommandRejection.InvalidValue, "Invalid capacitor step.");
            CapacitorSteps[id] = (int)steps; Revision++;
            _transitions.Add(new("CapacitorChanged", id, $"Capacitor steps {steps}", actor, GridChangeCause.Operator));
        }
        else if (command.Type is GridCommandType.ShedLoad or GridCommandType.RestoreLoad)
        {
            if (!Demand.ContainsKey(id)) return Reject(GridCommandRejection.UnknownTarget, "Unknown load.");
            if (command.Type == GridCommandType.ShedLoad) ShedLoads.Add(id); else ShedLoads.Remove(id);
            _transitions.Add(new(command.Type == GridCommandType.ShedLoad ? "LoadShed" : "LoadRestored", id, command.Type.ToString(), actor, GridChangeCause.Operator));
        }
        else if (command.Type == GridCommandType.ResetProtection)
        {
            if (!Relays.TryGetValue(id, out var relay)) return Reject(GridCommandRejection.UnknownTarget, "Unknown protection.");
            relay.Tripped = false; relay.Progress = 0; relay.Failed = false;
            SetAlarm(id, GridAlarmType.ProtectionTrip, false, AlarmSeverity.High, "Protection reset", 0, 0);
        }
        else if (command.Type == GridCommandType.AcknowledgeAlarm)
        {
            if (!_alarms.TryGetValue(id, out var alarm)) return Reject(GridCommandRejection.UnknownTarget, "Unknown alarm.");
            _alarms[id] = alarm with { Acknowledged = true, AcknowledgedBy = actor };
        }
        else return Reject(GridCommandRejection.InvalidValue, "Unsupported command.");
        Revision++; Solve(); RefreshMeasurements();
        return GridCommandResult.Ok("Applied.");
    }

    public GridCommandResult SetFault(string id, GridEquipmentFault fault, string actor)
    {
        var isSwitch = Switches.ContainsKey(id);
        var isBranch = Definition.Lines.Any(x => x.Id == id) || Definition.Transformers.Any(x => x.Id == id);
        var isBus = Definition.Buses.Any(x => x.Id == id);
        if (!isSwitch && !isBranch && !isBus) return Reject(GridCommandRejection.UnknownTarget, "Unknown equipment.");
        if (!Enum.IsDefined(fault) || fault == GridEquipmentFault.SwitchStuck && !isSwitch
            || fault is GridEquipmentFault.ThreePhaseShortCircuit or GridEquipmentFault.EarthFault && !isBus && !Definition.Lines.Any(x => x.Id == id))
            return Reject(GridCommandRejection.InvalidValue, "Fault incompatible with equipment.");
        if (fault == GridEquipmentFault.None) Faults.Remove(id); else Faults[id] = fault;
        _transitions.Add(new(fault == GridEquipmentFault.None ? "FaultCleared" : "FaultInjected", id, $"Fault {fault}", actor));
        Revision++; Solve(); RefreshMeasurements();
        return GridCommandResult.Ok("Fault updated.");
    }

    public GridCommandResult SetSpoof(string id, double? value, string actor)
    {
        if (!_measurements.ContainsKey(id)) return Reject(GridCommandRejection.UnknownTarget, "Unknown measurement.");
        if (value is { } number && !double.IsFinite(number)) return Reject(GridCommandRejection.InvalidValue, "Invalid value.");
        if (value is { } x) Spoofs[id] = x; else Spoofs.Remove(id);
        _transitions.Add(new("MeasurementSpoofed", id, value.HasValue ? "Reported value overridden" : "Override removed", actor));
        Revision++; RefreshMeasurements(); return GridCommandResult.Ok("Measurement updated.");
    }

    public GridCommandResult SetProtection(string id, double pickup, double setting, bool enabled, string actor)
    {
        if (!Relays.TryGetValue(id, out var relay)) return Reject(GridCommandRejection.UnknownTarget, "Unknown protection.");
        if (!double.IsFinite(pickup) || pickup <= 0 || !double.IsFinite(setting) || setting < 0)
            return Reject(GridCommandRejection.InvalidValue, "Invalid protection settings.");
        relay.Settings = relay.Settings with { PickupValue = pickup, TimeSetting = setting, Enabled = enabled };
        relay.Progress = 0; relay.PickedUp = false; relay.Failed = false;
        _transitions.Add(new("ProtectionSettingChanged", id, "Protection settings changed", actor, GridChangeCause.Unattributed));
        Revision++; return GridCommandResult.Ok("Protection updated.");
    }

    public GridCommandResult SetTap(string id, int tap, string? actor, GridChangeCause cause)
    {
        var t = Definition.Transformers.FirstOrDefault(t => t.Id == id && t.HasOnLoadTapChanger);
        if (t is null) return Reject(GridCommandRejection.UnknownTarget, "Unknown OLTC.");
        if (tap < t.TapMin || tap > t.TapMax) return Reject(GridCommandRejection.InvalidValue, "Tap out of range.");
        Taps[id] = tap; _avrTime[id] = 0; _avrDirection[id] = 0; Revision++;
        _transitions.Add(new("TapChanged", id, $"Tap position {tap}", actor, cause));
        Solve(); RefreshMeasurements(); return GridCommandResult.Ok("Tap updated.");
    }

    public GridCommandResult SetRtuOnline(string id, bool online, string actor)
    {
        if (!RtuOnline.ContainsKey(id)) return Reject(GridCommandRejection.UnknownTarget, "Unknown RTU.");
        RtuOnline[id] = online; Revision++;
        _transitions.Add(new("RtuOnlineChanged", id, online ? "RTU online" : "RTU offline", actor));
        UpdateAlarms(0); RefreshMeasurements(); return GridCommandResult.Ok("RTU updated.");
    }

    public GridCommandResult RestoreTrainingDefaults(string actor)
    {
        Faults.Clear(); Spoofs.Clear();
        foreach (var p in Definition.Protections)
        {
            Relays[p.Id].Settings = p; Relays[p.Id].Progress = 0; Relays[p.Id].Failed = false;
            Relays[p.Id].PickedUp = false; Relays[p.Id].Tripped = false;
            SetAlarm(p.Id, GridAlarmType.ProtectionTrip, false, AlarmSeverity.High, "Protection reset", 0, 0);
        }
        foreach (var id in RtuOnline.Keys) RtuOnline[id] = true;
        foreach (var t in Definition.Transformers)
        {
            Taps[t.Id] = t.InitialTap;
            TapAutomatic[t.Id] = t.HasOnLoadTapChanger && t.AvrTargetLvKv.HasValue;
            _avrTime[t.Id] = 0; _avrDirection[t.Id] = 0;
        }
        foreach (var c in Definition.Capacitors) CapacitorSteps[c.Id] = c.InitialSteps;
        _transitions.Add(new("FaultCleared", "grid", "Training defaults restored", actor));
        Revision++; Solve(); RefreshMeasurements(); return GridCommandResult.Ok("Training defaults restored.");
    }

    public GridSnapshot Snapshot(DateTimeOffset? now = null)
    {
        var buses = Definition.Buses.Select(b =>
        {
            var v = _flow.Voltages.GetValueOrDefault(b.Id);
            return new BusSnapshot(b.Id, b.Name, b.NominalVoltageKv, v.Magnitude > 0.01,
                v.Magnitude * b.NominalVoltageKv, v.Magnitude, v.Phase * 180 / Math.PI);
        }).ToArray();
        var branches = BranchSnapshots();
        var loads = Definition.Loads.Select(l =>
        {
            var bus = buses.First(b => b.Id == l.BusId);
            var on = bus.Energized && Connected(l.Id) && !ShedLoads.Contains(l.Id);
            var p = _flow.LoadPowersMw.GetValueOrDefault(l.Id);
            return new LoadSnapshot(l.Id, l.Name, l.BusId, on, Demand[l.Id], p, p * Math.Tan(Math.Acos(l.PowerFactor)),
                l.Customers, on ? l.Customers : 0, ShedLoads.Contains(l.Id), l.Critical);
        }).ToArray();
        var generators = Definition.Generators.Select(g =>
        {
            var on = Connected(g.Id) && buses.First(b => b.Id == g.BusId).Energized;
            return new GeneratorSnapshot(g.Id, g.Name, g.BusId, g.Kind, on, _flow.GeneratorPowersMw.GetValueOrDefault(g.Id), 0);
        }).ToArray();
        return new(Definition.Name, Revision, SimulationTime, StepCount, _frequency, _converged && !_flow.FaultActive, _iterations, buses, branches,
            Definition.Switches.Select(s => new SwitchSnapshot(s.Id, s.Name, s.Kind, s.BusId, s.ElementId, Switches[s.Id].Closed,
                Switches[s.Id].SelectedToClose.HasValue && (!now.HasValue || Switches[s.Id].SelectedUntil > now),
                !now.HasValue || Switches[s.Id].SelectedUntil > now ? Switches[s.Id].SelectedToClose : null, Switches[s.Id].LastOperatedBy,
                Switches[s.Id].LastCause, Faults.GetValueOrDefault(s.Id))).ToArray(), loads, generators,
            Definition.Capacitors.Select(c => new CapacitorSnapshot(c.Id, c.Name, c.BusId, CapacitorSteps[c.Id], c.Steps,
                Connected(c.Id) ? c.RatedPowerMvar * CapacitorSteps[c.Id] / c.Steps * Math.Pow(_flow.Voltages[c.BusId].Magnitude, 2) : 0)).ToArray(),
            Definition.Protections.Select(p =>
            {
                var r = Relays[p.Id]; var s = r.Settings;
                return new ProtectionSnapshot(p.Id, p.Name, p.SwitchId, s.Function, s.Curve, s.PickupValue, s.TimeSetting, s.Enabled,
                    r.PickedUp, r.Tripped, r.LastTripAt, s != p);
            }).ToArray(), _measurements.Values.ToArray(), _alarms.Values.ToArray(),
            new(loads.Sum(l => l.ActivePowerMw), generators.Sum(g => g.ActivePowerMw), _flow.SourceImportMw,
                branches.Sum(b => b.LossesMw) + _flow.SourceLossesMw, loads.Sum(l => l.Customers), loads.Sum(l => l.CustomersSupplied)));
    }

    private BranchSnapshot[] BranchSnapshots()
    {
        var result = new List<BranchSnapshot>();
        foreach (var (id, name, from, to, kind, rating) in Definition.Lines.Select(l => (l.Id, l.Name, l.FromBusId, l.ToBusId, GridBranchKind.Line, l.RatedCurrentA))
            .Concat(Definition.Transformers.Select(t => (t.Id, t.Name, t.HvBusId, t.LvBusId, GridBranchKind.Transformer, t.RatedPowerMva))))
        {
            var models = _flow.Branches.Where(b => b.Id == id).ToArray();
            var current = 0d; var sf = Complex.Zero; var st = Complex.Zero;
            if (models.Length > 0)
            {
                var first = models[0]; var last = models[^1];
                var powersAt = _flow.FaultActive ? _flow.PrefaultVoltages : _flow.NodeVoltages;
                sf = first.From == _flow.BusIndices[from] ? powersAt[first.From] * Complex.Conjugate(first.FromCurrent(powersAt)) * GridPowerFlowSolver.BaseMva : Complex.Zero;
                st = last.To == _flow.BusIndices[to] ? powersAt[last.To] * Complex.Conjugate(last.ToCurrent(powersAt)) * GridPowerFlowSolver.BaseMva : Complex.Zero;
                current = Math.Max(CurrentFor(id, from), CurrentFor(id, to));
            }
            var loading = kind == GridBranchKind.Line ? current / rating * 100 : Math.Max(sf.Magnitude, st.Magnitude) / rating * 100;
            result.Add(new(id, name, kind, from, to, models.Any(b => _flow.NodeVoltages[b.From].Magnitude > 0.01 || _flow.NodeVoltages[b.To].Magnitude > 0.01),
                current, sf.Real, sf.Imaginary,
                st.Real, st.Imaginary,
                Math.Max(0, sf.Real + st.Real), loading,
                Taps.TryGetValue(id, out var tap) ? tap : null, TapAutomatic.TryGetValue(id, out var auto) ? auto : null, Faults.GetValueOrDefault(id)));
        }
        return result.ToArray();
    }

    private double CurrentFor(string id, string busId)
    {
        var models = _flow.Branches.Where(b => b.Id == id).ToArray();
        if (models.Length == 0) return 0;
        var kv = Definition.Buses.First(b => b.Id == busId).NominalVoltageKv;
        var index = _flow.BusIndices[busId];
        var positive = models.Where(b => b.From == index || b.To == index)
            .Select(b => (b.From == index ? b.FromCurrent(_flow.NodeVoltages) : b.ToCurrent(_flow.NodeVoltages)).Magnitude
                * GridPowerFlowSolver.BaseMva * 1000 / (Math.Sqrt(3) * kv)).DefaultIfEmpty(0).Max();
        // Conservative maximum phase envelope: prefault + residual fault contribution.
        return positive + 2 * _flow.EarthCurrents.GetValueOrDefault($"{id}:{busId}") / 3;
    }

    private bool Solve()
    {
        var flow = _solver.Solve(this);
        _converged = flow.Converged; _iterations = flow.Iterations;
        if (flow.Converged) _flow = flow;
        foreach (var relay in Relays.Values)
            if (!flow.Converged || !relay.Settings.Enabled || ProtectionMultiple(relay.Settings) <= 1)
            { relay.Progress = 0; relay.PickedUp = false; relay.Failed = false; }
        foreach (var t in Definition.Transformers.Where(t => t.HasOnLoadTapChanger && t.AvrTargetLvKv.HasValue))
            if (!flow.Converged || !TapAutomatic[t.Id] || AvrDirection(t) != _avrDirection.GetValueOrDefault(t.Id))
            { _avrTime[t.Id] = 0; _avrDirection[t.Id] = 0; }
        SetAlarm("grid", GridAlarmType.SolverDivergence, !flow.Converged, AlarmSeverity.High, "Power flow failed; retaining last good state", 0, 0);
        return flow.Converged;
    }

    private bool UpdateDemand()
    {
        foreach (var l in Definition.Loads) Demand[l.Id] = l.PeakActivePowerMw * GridProfiles.Load(l.Profile, SimulationTime) * (l.Profile == GridLoadProfile.Constant ? 1 : GridProfiles.DemandVariation(SimulationTime));
        foreach (var g in Definition.Generators) Generation[g.Id] = g.RatedPowerMw * (g.Kind == GridGeneratorKind.Photovoltaic ? GridProfiles.Sun(SimulationTime) : 0.85);
        return true;
    }

    private bool RunProtection(double seconds, bool operate = true)
    {
        var changed = false;
        foreach (var (id, relay) in Relays)
        {
            if (!_converged) break;
            var p = relay.Settings; var sw = Definition.Switches.First(s => s.Id == p.SwitchId);
            var value = ProtectionMultiple(p);
            var picked = p.Enabled && Switches[sw.Id].Closed && value > 1;
            if (picked && !relay.PickedUp) _transitions.Add(new("ProtectionPickup", id, "Protection picked up", null, GridChangeCause.Protection));
            relay.PickedUp = picked;
            if (!picked) { relay.Progress = 0; relay.Failed = false; continue; }
            if (relay.Failed) continue;
            var duration = GridProtectionCurves.OperateSeconds(p.Curve, value, p.TimeSetting);
            relay.Progress += duration <= 0 ? 1 : seconds / duration;
            if (relay.Progress < 1 - 1e-8 || !operate) continue;
            var opened = OperateSwitch(sw.Id, false, null, GridChangeCause.Protection).Accepted;
            if (!opened) { relay.Failed = true; continue; }
            changed = true;
            relay.LastTripOperation = Switches[sw.Id].Operations;
            relay.Tripped = true; relay.LastTripAt = SimulationTime;
            _transitions.Add(new("ProtectionTripped", id, $"{p.Name} tripped {sw.Id}", null, GridChangeCause.Protection));
            SetAlarm(id, GridAlarmType.ProtectionTrip, true, AlarmSeverity.High, $"{p.Name} trip", 0, 0);
        }
        return changed;
    }

    private bool RunAvr(double seconds, bool operate = true)
    {
        if (!_converged) return false;
        var changed = false;
        foreach (var t in Definition.Transformers.Where(t => t.HasOnLoadTapChanger && TapAutomatic[t.Id] && t.AvrTargetLvKv.HasValue))
        {
            var direction = AvrDirection(t);
            if (direction == 0) { _avrTime[t.Id] = 0; _avrDirection[t.Id] = 0; continue; }
            if (_avrDirection.GetValueOrDefault(t.Id) != direction) _avrTime[t.Id] = 0;
            _avrDirection[t.Id] = direction;
            _avrTime[t.Id] = _avrTime.GetValueOrDefault(t.Id) + seconds;
            if (_avrTime[t.Id] < t.AvrDelaySeconds - 1e-8 || !operate) continue;
            var tap = Math.Clamp(Taps[t.Id] + direction, t.TapMin, t.TapMax);
            _avrTime[t.Id] = 0;
            if (tap != Taps[t.Id]) changed |= SetTap(t.Id, tap, null, GridChangeCause.Automatic).Accepted;
        }
        return changed;
    }

    private double ProtectionMultiple(GridProtectionDefinition p)
    {
        var sw = Definition.Switches.First(s => s.Id == p.SwitchId);
        if (!Switches[sw.Id].Closed) return 0;
        var voltage = _flow.Voltages[sw.BusId].Magnitude;
        return p.Function switch
        {
            GridProtectionFunction.Undervoltage27 => _flow.EnergizedBuses.Contains(sw.BusId)
                ? p.PickupValue / Math.Max(voltage, 1e-12) : 0,
            GridProtectionFunction.Overvoltage59 => voltage / p.PickupValue,
            GridProtectionFunction.EarthFault51N => _flow.EarthCurrents.GetValueOrDefault($"{sw.ElementId}:{sw.BusId}") / p.PickupValue,
            _ => CurrentFor(sw.ElementId, sw.BusId) / p.PickupValue
        };
    }

    private int AvrDirection(GridTransformerDefinition t)
    {
        var voltage = _flow.Voltages[t.LvBusId].Magnitude * Definition.Buses.First(b => b.Id == t.LvBusId).NominalVoltageKv;
        var error = t.AvrTargetLvKv!.Value - voltage;
        if (!_flow.EnergizedBuses.Contains(t.LvBusId) || _flow.FaultActive || Math.Abs(error) <= t.AvrTargetLvKv.Value * t.AvrDeadbandPercent / 200)
            return 0;
        var direction = Math.Sign(error);
        return Taps[t.Id] + direction < t.TapMin || Taps[t.Id] + direction > t.TapMax ? 0 : direction;
    }

    private bool UpdateAlarms(double seconds)
    {
        if (!_converged)
        {
            foreach (var r in RtuOnline) SetAlarm(r.Key, GridAlarmType.CommunicationLoss, !r.Value, AlarmSeverity.High, "RTU communication lost", seconds, 0);
            SetAlarm("grid", GridAlarmType.SolverDivergence, true, AlarmSeverity.High, "Power flow failed; retaining last good state", seconds, 0);
            return false;
        }
        foreach (var b in Definition.Buses)
        {
            var v = _flow.Voltages[b.Id].Magnitude;
            SetAlarm(b.Id, GridAlarmType.Undervoltage, _converged && _flow.EnergizedBuses.Contains(b.Id) && v < b.MinVoltagePu + (Active(b.Id, GridAlarmType.Undervoltage) ? 0.01 : 0), AlarmSeverity.Medium, "Bus undervoltage", seconds, 2);
            SetAlarm(b.Id, GridAlarmType.Overvoltage, _converged && v > b.MaxVoltagePu - (Active(b.Id, GridAlarmType.Overvoltage) ? 0.01 : 0), AlarmSeverity.Medium, "Bus overvoltage", seconds, 2);
        }
        foreach (var b in BranchSnapshots()) SetAlarm(b.Id, GridAlarmType.Overload, b.LoadingPercent > (Active(b.Id, GridAlarmType.Overload) ? 98 : 100),
            b.LoadingPercent > 110 ? AlarmSeverity.High : AlarmSeverity.Medium, "Equipment overload", seconds, 5);
        foreach (var l in Definition.Loads)
        {
            var lost = _flow.Voltages[l.BusId].Magnitude < 0.01 || !Connected(l.Id) || ShedLoads.Contains(l.Id);
            var before = Active(l.Id, GridAlarmType.LossOfSupply);
            SetAlarm(l.Id, GridAlarmType.LossOfSupply, lost, AlarmSeverity.High, "Load has lost supply", seconds, 0);
            if (lost != before) _transitions.Add(new(lost ? "SupplyLost" : "SupplyRestored", l.Id, lost ? "Supply lost" : "Supply restored"));
        }
        foreach (var r in RtuOnline) SetAlarm(r.Key, GridAlarmType.CommunicationLoss, !r.Value, AlarmSeverity.High, "RTU communication lost", seconds, 0);
        foreach (var t in Definition.Transformers.Where(t => t.HasOnLoadTapChanger))
            SetAlarm(t.Id, GridAlarmType.TapLimit, Taps[t.Id] == t.TapMin || Taps[t.Id] == t.TapMax, AlarmSeverity.Medium, "Tap at limit", seconds, 0);
        SetAlarm("grid", GridAlarmType.SolverDivergence, !_converged, AlarmSeverity.High, "Power flow failed; retaining last good state", seconds, 0);
        return true;
    }

    private bool Active(string id, GridAlarmType type) => _alarms.GetValueOrDefault($"{id}:{type}")?.Active == true;

    private bool SetAlarm(string id, GridAlarmType type, bool condition, AlarmSeverity severity, string message, double dt, double delay)
    {
        var key = $"{id}:{type}";
        _alarmTime[key] = condition ? _alarmTime.GetValueOrDefault(key) + dt : 0;
        var active = condition && _alarmTime[key] >= delay;
        var prior = _alarms.GetValueOrDefault(key);
        if (prior?.Active == active || prior is null && !active)
        {
            if (prior is not null && active) _alarms[key] = prior with { Severity = severity };
            return false;
        }
        _alarms[key] = active ? new(key, id, type, severity, message, SimulationTime, true, false, null) : prior! with { Active = false };
        _transitions.Add(new(active ? "AlarmRaised" : "AlarmCleared", id, message));
        return true;
    }

    private bool RefreshMeasurements()
    {
        var branches = BranchSnapshots();
        foreach (var rtu in Definition.Rtus)
        {
            foreach (var id in rtu.ElementIds)
            {
                if (Definition.Buses.FirstOrDefault(b => b.Id == id) is { } bus)
                    Measure(id, GridMeasurementKind.Voltage, "kV", _flow.Voltages[id].Magnitude * bus.NominalVoltageKv);
                if (branches.FirstOrDefault(b => b.Id == id) is { } branch)
                {
                    Measure(id, GridMeasurementKind.Current, "A", branch.CurrentA);
                    Measure(id, GridMeasurementKind.ActivePower, "MW", branch.ActivePowerFromMw);
                    Measure(id, GridMeasurementKind.ReactivePower, "Mvar", branch.ReactivePowerFromMvar);
                    if (branch.TapPosition is { } tap) Measure(id, GridMeasurementKind.TapPosition, "step", tap);
                }
                if (Switches.TryGetValue(id, out var sw)) Measure(id, GridMeasurementKind.SwitchState, "", sw.Closed ? 1 : 0);
                if (Definition.Loads.FirstOrDefault(l => l.Id == id) is { } load)
                {
                    var p = _flow.LoadPowersMw.GetValueOrDefault(id);
                    Measure(id, GridMeasurementKind.ActivePower, "MW", p);
                    Measure(id, GridMeasurementKind.ReactivePower, "Mvar", p * Math.Tan(Math.Acos(load.PowerFactor)));
                }
                if (Definition.Generators.FirstOrDefault(g => g.Id == id) is { } generator)
                    Measure(id, GridMeasurementKind.ActivePower, "MW", _flow.GeneratorPowersMw.GetValueOrDefault(id));
            }
            Measure(rtu.Id, GridMeasurementKind.Frequency, "Hz", _frequency);

            bool Measure(string elementId, GridMeasurementKind kind, string unit, double truth)
            {
                var key = $"{rtu.Id}:{elementId}:{kind}";
                var discrete = kind is GridMeasurementKind.SwitchState or GridMeasurementKind.TapPosition;
                var value = discrete ? truth : truth * (0.998 + _random.NextDouble() * 0.004);
                var spoofed = Spoofs.TryGetValue(key, out var spoof);
                var online = RtuOnline[rtu.Id];
                var reported = !online && _measurements.TryGetValue(key, out var previous) ? previous.ReportedValue : spoofed ? spoof : value;
                _measurements[key] = new(key, elementId, rtu.Id, kind, unit, reported, !online ? MeasurementQuality.Bad : !_converged || _flow.FaultActive && kind is GridMeasurementKind.ActivePower or GridMeasurementKind.ReactivePower ? MeasurementQuality.Uncertain : MeasurementQuality.Good, truth, spoofed, spoofed ? spoof : null);
                return true;
            }
        }
        return true;
    }

    private static GridCommandResult Reject(GridCommandRejection reason, string message) => GridCommandResult.Reject(reason, message);
}
