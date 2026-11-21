using Domain.Process.Devices;

namespace Domain.Process.Configuration;

/// <summary>
/// <see cref="TankId"/> is the tank fed by an inlet valve, or drained by an outlet/drain valve.
/// Discharge valves have no tank because their flow is governed by the pump they belong to.
/// </summary>
public sealed record ValveDefinition(
    string Id,
    string Name,
    ValveRole Role,
    string? TankId,
    double MaxFlowRate,
    double TravelPercentPerSecond,
    double FailSafePosition,
    double InitialPosition);
