using Domain.Process.Alarms;
using Domain.Process.Commands;
using Domain.Process.Configuration;
using Domain.Process.Devices;
using Domain.Process.Hydraulics;
using Domain.Process.Safety;
using Domain.Process.Snapshots;
using Domain.Simulation;

namespace Domain.Process;

/// <summary>
/// Aggregate root of the virtual water treatment plant. The class is not thread-safe: the application
/// runtime serialises every call.
/// </summary>
public sealed class Plant
{
    private readonly Dictionary<string, Tank> _tanks;
    private readonly List<Pump> _pumps;
    private readonly Dictionary<string, Valve> _valves;
    private readonly List<Sensor> _sensors;
    private readonly SafetyInterlockEvaluator _interlocks;
    private readonly AlarmBook _alarms = new();
    private readonly List<DeviceChange> _pendingChanges = [];

    public Plant(PlantDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = PlantDefinitionValidator.Validate(definition);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(definition));

        Definition = definition;
        _tanks = definition.Tanks.ToDictionary(t => t.Id, t => new Tank(t), StringComparer.Ordinal);
        _pumps = definition.Pumps.Select(p => new Pump(p)).ToList();
        _valves = definition.Valves.ToDictionary(v => v.Id, v => new Valve(v), StringComparer.Ordinal);
        _sensors = definition.Sensors.Select(s => new Sensor(s)).ToList();
        _interlocks = new SafetyInterlockEvaluator(definition.Limits);
        UpdateSensors();
    }

    public PlantDefinition Definition { get; }
    public SimulationClock Clock { get; } = new();
    public long Revision { get; private set; }
    public bool InterlocksBypassed { get; private set; }
    public IReadOnlyCollection<Tank> Tanks => _tanks.Values;
    public IReadOnlyList<Pump> Pumps => _pumps;
    public IReadOnlyCollection<Valve> Valves => _valves.Values;
    public IReadOnlyList<Sensor> Sensors => _sensors;

    public bool ContainsDevice(string id) =>
        _tanks.ContainsKey(id) || _valves.ContainsKey(id) || FindPump(id) is not null || FindSensor(id) is not null;

    public Pump? FindPump(string id) => _pumps.FirstOrDefault(p => p.Id == id);
    public Valve? FindValve(string id) => _valves.GetValueOrDefault(id);
    public Sensor? FindSensor(string id) => _sensors.FirstOrDefault(s => s.Id == id);
    public Tank? FindTank(string id) => _tanks.GetValueOrDefault(id);

    public PlantStepResult Step(TimeSpan step, DateTimeOffset now)
    {
        if (step <= TimeSpan.Zero || step > TimeSpan.FromSeconds(60))
            throw new ArgumentOutOfRangeException(nameof(step), "A simulation step must be between 0 and 60 seconds.");

        var seconds = step.TotalSeconds;
        HydraulicModel.Step(_tanks, _pumps, _valves, Definition.Limits, seconds);
        UpdateSensors();

        var activations = _interlocks.Evaluate(_pumps, _valves, _tanks);
        var violations = new List<SafetyViolation>();
        foreach (var activation in activations)
        {
            if (InterlocksBypassed)
            {
                violations.Add(new SafetyViolation(activation.DeviceId, activation.Reason));
                continue;
            }

            DeviceChange? change = FindPump(activation.DeviceId) is { } pump
                ? pump.Trip(activation.Reason)
                : FindValve(activation.DeviceId)?.ForceClosed(activation.Reason);
            if (change is not null)
                _pendingChanges.Add(change);
        }

        if (InterlocksBypassed)
            violations.AddRange(_tanks.Values.Where(t => t.IsOverflowing)
                .Select(t => new SafetyViolation(t.Id, $"{t.Definition.Name} overflowing with interlocks bypassed")));

        var conditions = AlarmConditionEvaluator.Evaluate(_tanks, _pumps, _valves, _sensors, Definition.Limits);
        var transitions = _alarms.Update(conditions, now);

        Clock.Advance(step);
        Revision++;

        var changes = _pendingChanges.ToArray();
        _pendingChanges.Clear();
        return new PlantStepResult(CaptureSnapshot(), changes,
            InterlocksBypassed ? [] : activations, violations, transitions);
    }

    public CommandResult CommandPump(string id, bool run, CommandOrigin origin)
    {
        if (FindPump(id) is not { } pump)
            return CommandResult.Rejected(id, CommandRejectionReason.UnknownDevice, $"Unknown pump {id}.");
        if (ModeMismatch(pump.Mode, origin) is { } mismatch)
            return mismatch with { DeviceId = id };
        if (run && pump.Fault != PumpFault.None)
            return CommandResult.Rejected(id, CommandRejectionReason.DeviceFaulted, $"{id} is faulted ({pump.Fault}).");
        if (run && pump.InterlockReason is not null && origin.Cause != ChangeCause.Unattributed && !pump.IsCommandedOn)
            return CommandResult.Rejected(id, CommandRejectionReason.InterlockActive,
                $"{id} interlock latched ({pump.InterlockReason}); reset required.");

        if (run && !InterlocksBypassed && origin.Cause != ChangeCause.Unattributed)
        {
            var decision = _interlocks.CanStartPump(pump, _tanks);
            if (!decision.Permitted)
                return CommandResult.Rejected(id, CommandRejectionReason.InterlockActive, decision.Reason!);
        }

        return Apply(pump.Command(run, origin), id, run ? $"{id} start commanded" : $"{id} stop commanded");
    }

    public CommandResult CommandValve(string id, double position, CommandOrigin origin)
    {
        if (FindValve(id) is not { } valve)
            return CommandResult.Rejected(id, CommandRejectionReason.UnknownDevice, $"Unknown valve {id}.");
        if (!double.IsFinite(position) || position is < 0 or > 100)
            return CommandResult.Rejected(id, CommandRejectionReason.InvalidValue, "Valve position must be between 0 and 100 percent.");
        if (ModeMismatch(valve.Mode, origin) is { } mismatch)
            return mismatch with { DeviceId = id };
        if (valve.Fault != ValveFault.None)
            return CommandResult.Rejected(id, CommandRejectionReason.DeviceFaulted, $"{id} is faulted ({valve.Fault}).");

        if (position > 0 && !InterlocksBypassed && origin.Cause != ChangeCause.Unattributed)
        {
            var decision = _interlocks.CanOpenInlet(valve, _tanks);
            if (!decision.Permitted)
                return CommandResult.Rejected(id, CommandRejectionReason.InterlockActive, decision.Reason!);
        }

        return Apply(valve.Command(position, origin), id, $"{id} positioned to {position:0.#}%");
    }

    public CommandResult SetMode(string id, ControlMode mode, CommandOrigin origin)
    {
        DeviceChange? change;
        if (FindPump(id) is { } pump)
            change = pump.SetMode(mode, origin);
        else if (FindValve(id) is { } valve)
            change = valve.SetMode(mode, origin);
        else
            return CommandResult.Rejected(id, CommandRejectionReason.UnknownDevice, $"Unknown actuator {id}.");

        return Apply(change, id, $"{id} switched to {mode} mode");
    }

    public CommandResult InjectFault(FaultInjection fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        DeviceChange? change;
        if (FindPump(fault.DeviceId) is { } pump)
            change = pump.SetFault(fault.PumpFault);
        else if (FindValve(fault.DeviceId) is { } valve)
            change = valve.SetFault(fault.ValveFault);
        else if (FindSensor(fault.DeviceId) is { } sensor)
            change = sensor.SetFault(fault.SensorFault, fault.DriftOffset);
        else
            return CommandResult.Rejected(fault.DeviceId, CommandRejectionReason.UnknownDevice, $"Unknown device {fault.DeviceId}.");

        return Apply(change, fault.DeviceId, $"Fault state of {fault.DeviceId} updated");
    }

    public CommandResult ClearFault(string id) => InjectFault(new FaultInjection(id));

    public CommandResult SpoofSensor(string id, double? value)
    {
        if (FindSensor(id) is not { } sensor)
            return CommandResult.Rejected(id, CommandRejectionReason.UnknownDevice, $"Unknown sensor {id}.");
        if (!sensor.Spoof(value))
            return CommandResult.Rejected(id, CommandRejectionReason.InvalidValue, "Spoofed value must be finite.");

        sensor.Update(sensor.TrueValue);
        Revision++;
        return CommandResult.Success(id, value is null ? $"{id} spoofing cleared" : $"{id} reports a spoofed value");
    }

    /// <summary>Instructor hook used to load scenario initial conditions.</summary>
    public CommandResult SetTankLevel(string id, double levelPercent)
    {
        if (FindTank(id) is not { } tank)
            return CommandResult.Rejected(id, CommandRejectionReason.UnknownDevice, $"Unknown tank {id}.");
        if (!double.IsFinite(levelPercent) || levelPercent is < 0 or > 100)
            return CommandResult.Rejected(id, CommandRejectionReason.InvalidValue, "Level must be between 0 and 100 percent.");

        tank.Restore(tank.Capacity * levelPercent / 100, tank.TotalOverflowVolume);
        UpdateSensors();
        Revision++;
        return CommandResult.Success(id, $"{id} level set to {levelPercent:0.#}%");
    }

    public CommandResult SetInterlockBypass(bool bypassed, string actor)
    {
        if (InterlocksBypassed == bypassed)
            return CommandResult.Success("SIS", "Interlock bypass unchanged");

        InterlocksBypassed = bypassed;
        _pendingChanges.Add(new DeviceChange("SIS", "Bypass", (!bypassed).ToString(), bypassed.ToString(),
            ChangeCause.Operator, actor));
        Revision++;
        return CommandResult.Success("SIS", bypassed ? "Safety interlocks bypassed" : "Safety interlocks restored");
    }

    /// <summary>Resets a latched interlock so the device may be restarted once the cause has cleared.</summary>
    public CommandResult ResetInterlock(string id)
    {
        var cleared = FindPump(id)?.ClearInterlock() ?? FindValve(id) is { InterlockReason: not null } valve && valve.Restore(
            valve.Position, valve.Setpoint, valve.Mode, valve.Fault, null);
        Revision++;
        return cleared
            ? CommandResult.Success(id, $"{id} interlock reset")
            : CommandResult.Rejected(id, CommandRejectionReason.InvalidValue, $"{id} has no latched interlock.");
    }

    /// <summary>Drives the listed actuators to their fail-safe state (pumps stop, valves move to the configured fail position).</summary>
    public int ApplyFailSafe(IEnumerable<string> deviceIds)
    {
        var count = 0;
        var origin = new CommandOrigin(ChangeCause.FailSafe, "watchdog");
        foreach (var id in deviceIds)
        {
            DeviceChange? change = FindPump(id) is { } pump
                ? pump.Command(false, origin)
                : FindValve(id) is { } valve ? valve.Command(valve.Definition.FailSafePosition, origin) : null;
            if (change is null)
                continue;

            _pendingChanges.Add(change);
            count++;
        }

        if (count > 0)
            Revision++;
        return count;
    }

    public AlarmTransition? AcknowledgeAlarm(string key, string actor, DateTimeOffset now) =>
        _alarms.Acknowledge(key, actor, now);

    public PlantSnapshot CaptureSnapshot() => new(
        Definition.Name,
        Revision,
        Clock.SimulationTime,
        Clock.StepCount,
        InterlocksBypassed,
        _tanks.Values.Select(t => new TankSnapshot(t.Id, t.Definition.Name, t.Capacity, Math.Round(t.Volume, 4),
            Math.Round(t.LevelPercent, 3), t.Definition.Thresholds, t.IsOverflowing, Math.Round(t.TotalOverflowVolume, 4))).ToArray(),
        _pumps.Select(p => new PumpSnapshot(p.Id, p.Definition.Name, p.Definition.SourceTankId, p.Definition.TargetTankId,
            p.Definition.DischargeValveId, p.State, p.Mode, p.Fault, Math.Round(p.Speed, 4), Math.Round(p.FlowRate, 4),
            p.DischargePressure, p.Health, p.InterlockReason, p.LastChangeCause, p.LastChangedBy, p.StartCount)).ToArray(),
        _valves.Values.Select(v => new ValveSnapshot(v.Id, v.Definition.Name, v.Definition.Role, v.Definition.TankId,
            Math.Round(v.Position, 3), v.Setpoint, v.Mode, v.Fault, Math.Round(v.FlowRate, 4), v.Health, v.InterlockReason,
            v.LastChangeCause, v.LastChangedBy)).ToArray(),
        _sensors.Select(s => new SensorSnapshot(s.Id, s.Definition.Name, s.Definition.Kind, s.Definition.DeviceId,
            s.Definition.Unit, Math.Round(s.ReportedValue, 4), s.Quality, Math.Round(s.TrueValue, 4), s.Fault,
            s.DriftOffset, s.IsSpoofed, s.SpoofedValue, s.Health)).ToArray(),
        _alarms.Current);

    /// <summary>Recreates a plant from a persisted snapshot. Unknown devices in the snapshot are ignored.</summary>
    public static Plant Restore(PlantDefinition definition, PlantSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var plant = new Plant(definition);
        foreach (var tank in snapshot.Tanks)
            plant.FindTank(tank.Id)?.Restore(tank.Volume, tank.TotalOverflowVolume);
        foreach (var pump in snapshot.Pumps)
            plant.FindPump(pump.Id)?.Restore(pump.State, pump.Mode, pump.Fault, pump.Speed, pump.StartCount, pump.InterlockReason);
        foreach (var valve in snapshot.Valves)
            plant.FindValve(valve.Id)?.Restore(valve.Position, valve.Setpoint, valve.Mode, valve.Fault, valve.InterlockReason);
        foreach (var sensor in snapshot.Sensors)
            plant.FindSensor(sensor.Id)?.Restore(sensor.Fault, sensor.DriftOffset, sensor.SpoofedValue, sensor.ReportedValue);

        plant.InterlocksBypassed = snapshot.InterlocksBypassed;
        plant.Clock.Restore(snapshot.SimulationTime, snapshot.StepCount);
        plant.Revision = snapshot.Revision;
        plant._alarms.Restore(snapshot.Alarms);
        plant.UpdateSensors();
        return plant;
    }

    private static CommandResult? ModeMismatch(ControlMode mode, CommandOrigin origin) => origin.Cause switch
    {
        ChangeCause.Operator when mode != ControlMode.Manual =>
            CommandResult.Rejected("", CommandRejectionReason.ModeMismatch, "Device is in automatic mode; switch it to manual first."),
        ChangeCause.Controller when mode != ControlMode.Automatic =>
            CommandResult.Rejected("", CommandRejectionReason.ModeMismatch, "Device is in manual mode; controller output ignored."),
        _ => null
    };

    private CommandResult Apply(DeviceChange? change, string id, string message)
    {
        if (change is null)
            return CommandResult.Success(id, $"{id} already in requested state");

        _pendingChanges.Add(change);
        Revision++;
        return CommandResult.Success(id, message);
    }

    private int UpdateSensors()
    {
        foreach (var sensor in _sensors)
        {
            var id = sensor.Definition.DeviceId;
            var value = sensor.Definition.Kind switch
            {
                SensorKind.Level => _tanks[id].LevelPercent,
                SensorKind.Pressure => FindPump(id)!.DischargePressure,
                SensorKind.Flow => FindPump(id)?.FlowRate ?? _valves[id].FlowRate,
                _ => 0
            };
            sensor.Update(value);
        }

        return _sensors.Count;
    }
}
