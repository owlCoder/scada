using System.Globalization;
using Domain.Process.Commands;
using Domain.Process.Configuration;

namespace Domain.Process.Devices;

public sealed class Valve
{
    public Valve(ValveDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Position = definition.InitialPosition;
        Setpoint = definition.InitialPosition;
    }

    public ValveDefinition Definition { get; }
    public string Id => Definition.Id;
    public ControlMode Mode { get; private set; } = ControlMode.Automatic;
    public ValveFault Fault { get; private set; } = ValveFault.None;

    /// <summary>Actual opening in percent.</summary>
    public double Position { get; private set; }

    /// <summary>Commanded opening in percent.</summary>
    public double Setpoint { get; private set; }
    public double FlowRate { get; private set; }
    public string? InterlockReason { get; private set; }
    public ChangeCause LastChangeCause { get; private set; }
    public string? LastChangedBy { get; private set; }

    public bool IsOpen => Position > 0.5;
    public DeviceHealth Health => Fault != ValveFault.None
        ? DeviceHealth.Faulted
        : InterlockReason is not null ? DeviceHealth.Warning : DeviceHealth.Healthy;

    public DeviceChange? Command(double setpoint, CommandOrigin origin)
    {
        if (!double.IsFinite(setpoint) || setpoint is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(setpoint));

        if (Math.Abs(Setpoint - setpoint) < 0.001)
            return null;

        var previous = Setpoint;
        Setpoint = setpoint;
        if (origin.Cause != ChangeCause.Interlock)
            InterlockReason = null;
        LastChangeCause = origin.Cause;
        LastChangedBy = origin.Actor;
        return new DeviceChange(Id, "Setpoint", Format(previous), Format(setpoint), origin.Cause, origin.Actor);
    }

    public DeviceChange? ForceClosed(string reason)
    {
        InterlockReason = reason;
        return Command(0, CommandOrigin.Interlock);
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

    public DeviceChange? SetFault(ValveFault fault)
    {
        if (Fault == fault)
            return null;

        var previous = Fault;
        Fault = fault;
        Position = fault switch
        {
            ValveFault.StuckOpen => 100,
            ValveFault.StuckClosed => 0,
            _ => Position
        };
        return new DeviceChange(Id, "Fault", previous.ToString(), fault.ToString(), ChangeCause.None, "simulation");
    }

    /// <summary>Moves the actual position toward the setpoint at the configured travel rate.</summary>
    public double AdvanceTravel(double seconds)
    {
        if (Fault != ValveFault.None)
            return Position;

        var maxStep = Definition.TravelPercentPerSecond * seconds;
        var difference = Setpoint - Position;
        Position = Math.Abs(difference) <= maxStep ? Setpoint : Position + Math.Sign(difference) * maxStep;
        return Position;
    }

    public double UpdateFlow(double flowRate)
    {
        FlowRate = flowRate;
        return FlowRate;
    }

    public bool Restore(double position, double setpoint, ControlMode mode, ValveFault fault, string? interlockReason)
    {
        if (position is < 0 or > 100 || setpoint is < 0 or > 100)
            return false;

        Position = position;
        Setpoint = setpoint;
        Mode = mode;
        Fault = fault;
        InterlockReason = interlockReason;
        return true;
    }

    private static string Format(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
