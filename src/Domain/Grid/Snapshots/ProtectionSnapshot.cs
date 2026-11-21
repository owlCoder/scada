using Domain.Grid.Definition;

namespace Domain.Grid.Snapshots;

/// <summary><see cref="SettingsChanged"/> is true when the current settings differ from the definition.</summary>
public sealed record ProtectionSnapshot(
    string Id,
    string Name,
    string SwitchId,
    GridProtectionFunction Function,
    GridCurveType Curve,
    double PickupValue,
    double TimeSetting,
    bool Enabled,
    bool PickedUp,
    bool Tripped,
    TimeSpan? LastTripAt,
    bool SettingsChanged);
