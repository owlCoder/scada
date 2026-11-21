using Domain.Network;

namespace Domain.Control;

/// <summary>
/// Common scan-cycle behaviour of simulated controllers: acquire inputs over the field link, run logic,
/// write outputs, and latch into a safe state after repeated communication failures.
/// </summary>
public abstract class VirtualController
{
    private double _accumulatedMilliseconds;

    protected VirtualController(ControllerDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (definition.ScanCycleMilliseconds <= 0 || definition.TimeoutThreshold <= 0)
            throw new ArgumentException("Scan cycle and timeout threshold must be positive.", nameof(definition));
        if (definition.Inputs.Concat(definition.Outputs).Any(p => !ControllerRegisterBank.IsValidAddress(p.Address)))
            throw new ArgumentException("I/O points must use valid register addresses.", nameof(definition));
        LoadDefaultSetpoints();
    }

    public ControllerDefinition Definition { get; }
    public string Id => Definition.Id;
    public ControllerRegisterBank Registers { get; } = new();
    public ControllerState State { get; private set; } = ControllerState.Running;
    public CommunicationState FieldCommunication { get; private set; } = CommunicationState.Online;
    public CommunicationState SupervisoryCommunication { get; private set; } = CommunicationState.Online;
    public long ScanCount { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public long TotalFailures { get; private set; }
    public long CommandsIssued { get; private set; }
    public TimeSpan LastScanAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Returns how many scans are due after the simulation advanced by <paramref name="step"/>.</summary>
    public int AccumulateScans(TimeSpan step)
    {
        _accumulatedMilliseconds += step.TotalMilliseconds;
        var due = (int)(_accumulatedMilliseconds / Definition.ScanCycleMilliseconds);
        _accumulatedMilliseconds -= due * Definition.ScanCycleMilliseconds;
        return due;
    }

    public ControllerScanResult Scan(IControllerFieldBus bus, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(bus);
        if (State is ControllerState.Stopped or ControllerState.Faulted)
            return new(Id, false, [], 0, false, false, false, []);

        ScanCount++;
        LastScanAt = now;
        var inputs = bus.ReadInputs(Definition);
        if (!inputs.Success)
            return HandleFieldFailure(inputs.Outcome);

        var restored = FieldCommunication == CommunicationState.Offline;
        ConsecutiveFailures = 0;
        FieldCommunication = CommunicationState.Online;
        StoreInputs(inputs);

        var commands = State == ControllerState.Running ? ComputeOutputs(inputs) : [];
        StoreOutputs(inputs, commands);
        var delivered = 0;
        foreach (var command in commands)
        {
            if (bus.WriteOutput(Definition, command) == TransmissionOutcome.Delivered)
            {
                delivered++;
                CommandsIssued++;
            }
            else
            {
                TotalFailures++;
                LastError = $"Output to {command.DeviceId} was not delivered";
            }
        }

        UpdateStatusRegisters();
        return new(Id, true, commands, delivered, false, restored, false, []);
    }

    public bool Start()
    {
        if (State != ControllerState.Stopped)
            return false;

        State = ControllerState.Running;
        UpdateStatusRegisters();
        return true;
    }

    public bool Stop()
    {
        if (State == ControllerState.Stopped)
            return false;

        State = ControllerState.Stopped;
        UpdateStatusRegisters();
        return true;
    }

    /// <summary>Clears a latched safe state or fault once the operator has confirmed the cause is resolved.</summary>
    public bool Reset()
    {
        if (State is not (ControllerState.SafeState or ControllerState.Faulted))
            return false;

        State = ControllerState.Running;
        ConsecutiveFailures = 0;
        LastError = null;
        UpdateStatusRegisters();
        return true;
    }

    public bool SetFaulted(string reason)
    {
        State = ControllerState.Faulted;
        LastError = reason;
        UpdateStatusRegisters();
        return true;
    }

    public bool SetSupervisoryCommunication(CommunicationState state)
    {
        var changed = SupervisoryCommunication != state;
        SupervisoryCommunication = state;
        return changed;
    }

    public bool WriteSetpoint(ushort address, ushort value) =>
        address is >= ControllerRegisterMap.SetpointBase and < ControllerRegisterMap.StatusBase &&
        Registers.Write(RegisterArea.Holding, address, value);

    public int RestoreDefaults()
    {
        var released = Registers.ReleaseAll();
        LoadDefaultSetpoints();
        return released;
    }

    public bool Restore(ControllerState state, IEnumerable<(ushort Address, ushort Value)> setpoints)
    {
        State = state;
        foreach (var (address, value) in setpoints)
            WriteSetpoint(address, value);
        UpdateStatusRegisters();
        return true;
    }

    public ControllerSnapshot Snapshot() => new(Id, Definition.Name, Definition.Kind, State, FieldCommunication,
        SupervisoryCommunication, Definition.ScanCycleMilliseconds, ScanCount, ConsecutiveFailures, TotalFailures,
        CommandsIssued, LastScanAt, LastError, Definition.ActuatorIds,
        Definition.Inputs.Select(i => i.DeviceId).Distinct(StringComparer.Ordinal).ToArray(),
        DescribeRegisters(), Registers.Forced.Count);

    public bool TryReadMeasurement(string sensorId, out double value)
    {
        var point = Definition.Inputs.FirstOrDefault(p => p.DeviceId == sensorId && p.Signal == IoSignal.Measurement);
        if (point is null)
        {
            value = 0;
            return false;
        }

        value = Registers.Read(RegisterArea.Input, point.Address) / point.Scale;
        return true;
    }

    public double? ReadCommandedOutput(string deviceId)
    {
        var point = Definition.Outputs.FirstOrDefault(p => p.DeviceId == deviceId);
        return point is null ? null : Registers.Read(RegisterArea.Holding, point.Address) / point.Scale;
    }

    protected abstract IReadOnlyList<ActuatorCommand> ComputeOutputs(FieldReadResult inputs);

    protected static ushort ToRegister(double value, double scale) =>
        (ushort)Math.Clamp(Math.Round(value * scale), 0, ushort.MaxValue);

    private ControllerScanResult HandleFieldFailure(TransmissionOutcome outcome)
    {
        ConsecutiveFailures++;
        TotalFailures++;
        LastError = $"Field read failed: {outcome}";
        var lost = false;
        var enteredSafeState = false;

        if (ConsecutiveFailures >= Definition.TimeoutThreshold)
        {
            lost = FieldCommunication != CommunicationState.Offline;
            FieldCommunication = CommunicationState.Offline;
            if (State == ControllerState.Running)
            {
                State = ControllerState.SafeState;
                enteredSafeState = true;
            }
        }
        else
        {
            FieldCommunication = CommunicationState.Degraded;
        }

        UpdateStatusRegisters();
        return new(Id, true, [], 0, lost, false, enteredSafeState,
            enteredSafeState ? Definition.ActuatorIds : []);
    }

    private int StoreInputs(FieldReadResult inputs)
    {
        var stored = 0;
        foreach (var point in Definition.Inputs.Where(p => p.Signal == IoSignal.Measurement))
        {
            if (!inputs.Measurements.TryGetValue(point.DeviceId, out var value))
                continue;

            Registers.Write(RegisterArea.Input, point.Address, ToRegister(value, point.Scale));
            stored++;
        }

        return stored;
    }

    private int StoreOutputs(FieldReadResult inputs, IReadOnlyList<ActuatorCommand> commands)
    {
        foreach (var point in Definition.Outputs)
        {
            var value = commands.FirstOrDefault(c => c.DeviceId == point.DeviceId)?.Value
                ?? (inputs.Actuators.TryGetValue(point.DeviceId, out var feedback) ? feedback.Value : 0);
            Registers.Write(RegisterArea.Holding, point.Address, ToRegister(value, point.Scale));
        }

        return Definition.Outputs.Count;
    }

    private int LoadDefaultSetpoints()
    {
        foreach (var loop in Definition.PumpLoops)
        {
            Registers.Write(RegisterArea.Holding, loop.StartLevelRegister, ToRegister(loop.DefaultStartLevel, 10));
            Registers.Write(RegisterArea.Holding, loop.StopLevelRegister, ToRegister(loop.DefaultStopLevel, 10));
        }

        foreach (var loop in Definition.ValveLoops)
        {
            Registers.Write(RegisterArea.Holding, loop.OpenBelowRegister, ToRegister(loop.DefaultOpenBelow, 10));
            Registers.Write(RegisterArea.Holding, loop.CloseAboveRegister, ToRegister(loop.DefaultCloseAbove, 10));
        }

        return Definition.PumpLoops.Count + Definition.ValveLoops.Count;
    }

    private int UpdateStatusRegisters()
    {
        Registers.Write(RegisterArea.Holding, ControllerRegisterMap.ControllerState, (ushort)State);
        Registers.Write(RegisterArea.Holding, ControllerRegisterMap.FieldCommunication, (ushort)FieldCommunication);
        Registers.Write(RegisterArea.Holding, ControllerRegisterMap.ScanCounter, (ushort)(ScanCount % 65536));
        Registers.Write(RegisterArea.Holding, ControllerRegisterMap.ConsecutiveFailures, (ushort)Math.Min(ConsecutiveFailures, ushort.MaxValue));
        return 4;
    }

    private IReadOnlyList<RegisterSnapshot> DescribeRegisters()
    {
        var registers = new List<RegisterSnapshot>();
        foreach (var point in Definition.Inputs)
            registers.Add(Describe(RegisterArea.Input, point.Address, point.Tag));
        foreach (var point in Definition.Outputs)
            registers.Add(Describe(RegisterArea.Holding, point.Address, point.Tag));
        foreach (var loop in Definition.PumpLoops)
        {
            registers.Add(Describe(RegisterArea.Holding, loop.StartLevelRegister, $"{loop.Name} start level x10"));
            registers.Add(Describe(RegisterArea.Holding, loop.StopLevelRegister, $"{loop.Name} stop level x10"));
        }
        foreach (var loop in Definition.ValveLoops)
        {
            registers.Add(Describe(RegisterArea.Holding, loop.OpenBelowRegister, $"{loop.Name} open below x10"));
            registers.Add(Describe(RegisterArea.Holding, loop.CloseAboveRegister, $"{loop.Name} close above x10"));
        }

        registers.Add(Describe(RegisterArea.Holding, ControllerRegisterMap.ControllerState, "Controller state"));
        registers.Add(Describe(RegisterArea.Holding, ControllerRegisterMap.FieldCommunication, "Field communication state"));
        registers.Add(Describe(RegisterArea.Holding, ControllerRegisterMap.ScanCounter, "Scan counter"));
        registers.Add(Describe(RegisterArea.Holding, ControllerRegisterMap.ConsecutiveFailures, "Consecutive failures"));
        return registers;
    }

    private RegisterSnapshot Describe(RegisterArea area, ushort address, string tag) =>
        new(area, address, tag, Registers.Read(area, address), Registers.IsForced(area, address));
}
