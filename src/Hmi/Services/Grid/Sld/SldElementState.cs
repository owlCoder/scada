using Application.Grid;
using Domain.Grid.Snapshots;
using Domain.Process.Alarms;
using Domain.Process.Devices;

namespace Hmi.Services.Grid.Sld;

/// <summary>
/// Dynamic, per-tick visual state of one element. Records give value equality so child components can skip re-renders.
/// <see cref="KeyText"/> is the compact value shown at low/medium zoom (bus kV, branch A·%, transformer loading);
/// <see cref="ValueText"/> is the full value box content at high zoom (kV, A·%, MW, switch state, tap).
/// </summary>
public sealed record SldElementState(
    string Id,
    bool Energized,
    bool? Closed,
    bool Invalid,
    string? QualityFlag,
    string? ValueText,
    double? LoadingPercent,
    bool HasFlow,
    bool FlowFrom,
    bool ProtectionTripped,
    AlarmSeverity? AlarmSeverity,
    bool AlarmUnacknowledged,
    IReadOnlyList<GridTag> Tags,
    bool Selected,
    string? KeyText = null)
{
    public string LoadingClass => LoadingPercent is { } p ? p <= 75 ? "heat-ok" : p <= 90 ? "heat-warn" : p <= 100 ? "heat-high" : "heat-over" : "heat-ok";

    public string AlarmClass
    {
        get
        {
            var severity = AlarmSeverity;
            return severity == Domain.Process.Alarms.AlarmSeverity.Critical ? "p1"
                : severity == Domain.Process.Alarms.AlarmSeverity.High ? "p2"
                : severity == Domain.Process.Alarms.AlarmSeverity.Medium ? "p3"
                : "p4";
        }
    }
}
