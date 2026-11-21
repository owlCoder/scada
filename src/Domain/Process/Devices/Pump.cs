using Domain.Process.Commands;
using Domain.Process.Configuration;

namespace Domain.Process.Devices;

public sealed class Pump
{
    public Pump(PumpDefinition definition) =>
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));

    public PumpDefinition Definition { get; }
    public string Id => Definition.Id;
    public ControlMode Mode { get; private set; } = ControlMode.Automatic;
    public PumpState State { get; private set; } = PumpState.Stopped;
    public PumpFault Fault { get; private set; } = PumpFault.None;

    /// <summary>Relative speed between 0 and 1 while ramping up or down.</summary>
    public double Speed { get; private set; }
    public double FlowRate { get; private set; }
    public double DischargePressure { get; private set; }
    public double DeadheadSeconds { get; private set; }
    public string? InterlockReason { get; private set; }
    public ChangeCause LastChangeCause { get; private set; }
    public string? LastChangedBy { get; private set; }
    public long StartCount { get; private set; }

    public bool IsCommandedOn => State is PumpState.Starting or PumpState.Running;
    public bool IsRotating => Speed > 0;

    public DeviceHealth Health => Fault != PumpFault.None
        ? DeviceHealth.Faulted
        : InterlockReason is not null ? DeviceHealth.Warning : DeviceHealth.Healthy;

    public DeviceChange? Command(bool run, CommandOrigin origin)
    {
        if (run == IsCommandedOn)
            return null;

        var previous = State;
        State = run ? PumpState.Starting : PumpState.Stopping;
        if (run)
        {
            StartCount++;
            InterlockReason = null;
        }

        return Record(previous, origin);
    }

    public DeviceChange? Trip(string reason)
    {
        InterlockReason = reason;
        if (!IsCommandedOn)
            return null;

        var previous = State;
        State = PumpState.Stopping;
        return Record(previous, CommandOrigin.Interlock);
    }

    public DeviceChange? SetMode(ControlMode mode, CommandOrigin origin)
    {
        if (Mode == mode)
            return null;

        var previous = Mode;
        Mode = mode;
        LastChangeCause = origin.Cause;
        LastChangedBy = origin.Actor;
        return new DeviceChange(Id, "Mode", previous.ToString(), mode.ToString(), origin.Cause, origin.Actor);
    }

    public DeviceChange? SetFault(PumpFault fault)
    {
        if (Fault == fault)
            return null;

        var previous = Fault;
        Fault = fault;
        if (fault != PumpFault.None)
        {
            State = PumpState.Stopped;
            Speed = 0;
            FlowRate = 0;
        }

        return new DeviceChange(Id, "Fault", previous.ToString(), fault.ToString(), ChangeCause.None, "simulation");
    }

    /// <summary>Advances the ramp and returns the resulting state.</summary>
    public PumpState AdvanceRamp(double seconds)
    {
        var delta = seconds / Definition.RampSeconds;
        switch (State)
        {
            case PumpState.Starting:
                Speed = Math.Min(1, Speed + delta);
                if (Speed >= 1)
                    State = PumpState.Running;
                break;
            case PumpState.Stopping:
                Speed = Math.Max(0, Speed - delta);
                if (Speed <= 0)
                    State = PumpState.Stopped;
                break;
        }

        return State;
    }

    public double UpdateHydraulics(double flowRate, double dischargePressure, bool deadheaded, double seconds)
    {
        FlowRate = flowRate;
        DischargePressure = dischargePressure;
        DeadheadSeconds = deadheaded ? DeadheadSeconds + seconds : 0;
        return FlowRate;
    }

    public bool ClearInterlock()
    {
        var had = InterlockReason is not null;
        InterlockReason = null;
        return had;
    }

    public bool Restore(PumpState state, ControlMode mode, PumpFault fault, double speed, long startCount, string? interlockReason)
    {
        State = state;
        Mode = mode;
        Fault = fault;
        Speed = Math.Clamp(speed, 0, 1);
        StartCount = startCount;
        InterlockReason = interlockReason;
        return true;
    }

    private DeviceChange Record(PumpState previous, CommandOrigin origin)
    {
        LastChangeCause = origin.Cause;
        LastChangedBy = origin.Actor;
        return new DeviceChange(Id, "State", previous.ToString(), State.ToString(), origin.Cause, origin.Actor);
    }
}
