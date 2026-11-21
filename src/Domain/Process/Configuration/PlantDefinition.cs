namespace Domain.Process.Configuration;

/// <summary>
/// Static description of the plant topology. Volumes are cubic metres, flow rates cubic metres per minute,
/// pressures bar and levels percent of capacity.
/// </summary>
public sealed record PlantDefinition(
    string Name,
    IReadOnlyList<TankDefinition> Tanks,
    IReadOnlyList<PumpDefinition> Pumps,
    IReadOnlyList<ValveDefinition> Valves,
    IReadOnlyList<SensorDefinition> Sensors,
    ProcessLimits Limits);
