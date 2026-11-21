namespace Application.Scenarios;

public sealed record ScenarioRunRecord(
    Guid RunId,
    string ScenarioId,
    string ScenarioName,
    string Category,
    ScenarioRunState State,
    string StartedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    TimeSpan SimulationStart,
    TimeSpan Elapsed,
    TimeSpan Duration,
    double ProgressPercent,
    IReadOnlyList<ScenarioActionLog> Actions,
    IReadOnlyDictionary<string, double> Measurements,
    IReadOnlyList<string> ExpectedDetections,
    IReadOnlyList<string> ObservedDetections,
    ScenarioOutcome Outcome,
    Guid? ReplayOf);
