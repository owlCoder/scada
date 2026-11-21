using Application.Scenarios;

namespace Infrastructure.Persistence.Postgres.Entities;

public sealed class ScenarioRunEntity
{
    public Guid RunId { get; set; }
    public string ScenarioId { get; set; } = "";
    public string ScenarioName { get; set; } = "";
    public string Category { get; set; } = "";
    public ScenarioRunState State { get; set; }
    public string StartedBy { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public TimeSpan SimulationStart { get; set; }
    public TimeSpan Elapsed { get; set; }
    public TimeSpan Duration { get; set; }
    public double ProgressPercent { get; set; }
    public string Actions { get; set; } = "[]";
    public string Measurements { get; set; } = "{}";
    public string ExpectedDetections { get; set; } = "[]";
    public string ObservedDetections { get; set; } = "[]";
    public ScenarioOutcome Outcome { get; set; }
    public Guid? ReplayOf { get; set; }

    public ScenarioRunEntity Apply(ScenarioRunRecord r)
    {
        RunId = r.RunId;
        ScenarioId = r.ScenarioId;
        ScenarioName = r.ScenarioName;
        Category = r.Category;
        State = r.State;
        StartedBy = r.StartedBy;
        StartedAt = Timestamps.Utc(r.StartedAt);
        EndedAt = Timestamps.Utc(r.EndedAt);
        SimulationStart = r.SimulationStart;
        Elapsed = r.Elapsed;
        Duration = r.Duration;
        ProgressPercent = r.ProgressPercent;
        Actions = PersistenceJson.Serialize(r.Actions);
        Measurements = PersistenceJson.Serialize(r.Measurements);
        ExpectedDetections = PersistenceJson.Serialize(r.ExpectedDetections);
        ObservedDetections = PersistenceJson.Serialize(r.ObservedDetections);
        Outcome = r.Outcome;
        ReplayOf = r.ReplayOf;
        return this;
    }

    public ScenarioRunRecord ToRecord() => new(RunId, ScenarioId, ScenarioName, Category, State, StartedBy, StartedAt, EndedAt,
        SimulationStart, Elapsed, Duration, ProgressPercent,
        PersistenceJson.Deserialize<ScenarioActionLog[]>(Actions),
        PersistenceJson.Deserialize<Dictionary<string, double>>(Measurements),
        PersistenceJson.Deserialize<string[]>(ExpectedDetections),
        PersistenceJson.Deserialize<string[]>(ObservedDetections),
        Outcome, ReplayOf);
}
