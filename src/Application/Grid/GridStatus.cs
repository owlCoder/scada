namespace Application.Grid;

public sealed record GridStatus(
    bool Paused,
    TimeSpan SimulationTime,
    long StepCount,
    double StepSeconds,
    double TimeScale,
    string DefinitionName,
    int DefinitionVersion);
