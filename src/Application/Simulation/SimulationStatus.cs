namespace Application.Simulation;

public sealed record SimulationStatus(
    bool IsPaused,
    TimeSpan SimulationTime,
    long StepCount,
    double StepSeconds,
    double TimeScale,
    int RandomSeed,
    long Revision);
