namespace Domain.Control;

/// <summary>
/// Hysteresis level control with duty/standby pump selection. Start and stop levels live in holding
/// registers so they can be tuned (or tampered with) at runtime.
/// </summary>
public sealed record PumpControlLoop(
    string Name,
    string LevelSensorId,
    IReadOnlyList<string> PumpIds,
    ushort StartLevelRegister,
    ushort StopLevelRegister,
    double DefaultStartLevel,
    double DefaultStopLevel,
    string? SourceLevelSensorId,
    double SourceMinimumLevel);
