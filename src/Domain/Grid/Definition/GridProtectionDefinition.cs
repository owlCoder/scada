namespace Domain.Grid.Definition;

/// <summary>
/// A relay function that trips <see cref="SwitchId"/> (which must be a breaker). <see cref="PickupValue"/> is in amperes
/// for overcurrent and earth fault, and per unit for under/overvoltage. For inverse curves <see cref="TimeSetting"/> is the
/// time multiplier; for definite time it is the delay in seconds.
/// </summary>
public sealed record GridProtectionDefinition(
    string Id,
    string Name,
    string SwitchId,
    GridProtectionFunction Function,
    GridCurveType Curve,
    double PickupValue,
    double TimeSetting,
    bool Enabled);
