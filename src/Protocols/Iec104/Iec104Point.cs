using Domain.Grid.Snapshots;

namespace Protocols.Iec104;

/// <summary>
/// One mapped point of an RTU. Commands use the same IOA as the status point: a switch command addresses the switch's
/// double point IOA, a tap command the tap IOA.
/// </summary>
public sealed record Iec104Point(
    int InformationObjectAddress,
    Iec104PointKind Kind,
    int CommonAddress,
    string RtuId,
    string ElementId,
    string ElementName,
    GridMeasurementKind? MeasurementKind,
    int MeasurementIndex,
    double Range,
    bool Controllable);
