using Domain.Process.Commands;
using Domain.Process.Devices;

namespace Domain.Process.Snapshots;

public sealed record PumpSnapshot(
    string Id,
    string Name,
    string SourceTankId,
    string TargetTankId,
    string DischargeValveId,
    PumpState State,
    ControlMode Mode,
    PumpFault Fault,
    double Speed,
    double FlowRate,
    double DischargePressure,
    DeviceHealth Health,
    string? InterlockReason,
    ChangeCause LastChangeCause,
    string? LastChangedBy,
    long StartCount);
