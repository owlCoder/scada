using Domain.Process.Commands;
using Domain.Process.Devices;

namespace Domain.Process.Snapshots;

public sealed record ValveSnapshot(
    string Id,
    string Name,
    ValveRole Role,
    string? TankId,
    double Position,
    double Setpoint,
    ControlMode Mode,
    ValveFault Fault,
    double FlowRate,
    DeviceHealth Health,
    string? InterlockReason,
    ChangeCause LastChangeCause,
    string? LastChangedBy);
