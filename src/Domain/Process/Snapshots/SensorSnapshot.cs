using Domain.Process.Devices;

namespace Domain.Process.Snapshots;

/// <summary>
/// <see cref="TrueValue"/>, <see cref="Fault"/> and <see cref="IsSpoofed"/> are ground truth for instructors;
/// operators should rely only on <see cref="ReportedValue"/> and <see cref="Quality"/>.
/// </summary>
public sealed record SensorSnapshot(
    string Id,
    string Name,
    SensorKind Kind,
    string DeviceId,
    string Unit,
    double ReportedValue,
    MeasurementQuality Quality,
    double TrueValue,
    SensorFault Fault,
    double DriftOffset,
    bool IsSpoofed,
    double? SpoofedValue,
    DeviceHealth Health);
