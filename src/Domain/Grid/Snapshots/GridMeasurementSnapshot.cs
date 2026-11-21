using Domain.Process.Devices;

namespace Domain.Grid.Snapshots;

/// <summary>
/// One telemetered value as reported by an RTU. <see cref="TrueValue"/> and <see cref="IsSpoofed"/> are ground truth for
/// instructors; operators rely on <see cref="ReportedValue"/> and <see cref="Quality"/>. Switch state is 1 closed, 0 open.
/// </summary>
public sealed record GridMeasurementSnapshot(
    string Id,
    string ElementId,
    string RtuId,
    GridMeasurementKind Kind,
    string Unit,
    double ReportedValue,
    MeasurementQuality Quality,
    double TrueValue,
    bool IsSpoofed,
    double? SpoofedValue);
