using Application.Scenarios;

namespace Security.Grid.Scenarios;

/// <summary>Mutable state of the active grid run. Only <see cref="GridScenarioEngine"/> touches it, under its run gate.</summary>
internal sealed class GridScenarioRun(
    GridScenarioDefinition definition,
    GridScenarioExecutionContext context,
    GridScenarioMeasurements measurements,
    string startedBy,
    DateTimeOffset startedAt,
    TimeSpan simulationStart,
    Guid? replayOf)
{
    private readonly List<ScenarioActionLog> _actions = [];
    private readonly ScheduledGridScenarioAction[] _schedule = definition.Actions.OrderBy(a => a.AtSeconds).ToArray();
    private int _nextAction;

    public GridScenarioDefinition Definition { get; } = definition;
    public GridScenarioExecutionContext Context { get; } = context;
    public GridScenarioMeasurements Measurements { get; } = measurements;
    public Guid RunId => Context.RunId;
    public ScenarioRunState State { get; set; } = ScenarioRunState.Running;
    public double ElapsedSeconds { get; set; }
    public int StepsSincePublish { get; set; }

    /// <summary>Returns the next scheduled action whose time has come, in schedule order, or null.</summary>
    public ScheduledGridScenarioAction? TakeDue() =>
        _nextAction < _schedule.Length && _schedule[_nextAction].AtSeconds <= ElapsedSeconds ? _schedule[_nextAction++] : null;

    public bool IsComplete => ElapsedSeconds >= Definition.DurationSeconds;

    public int Log(ScenarioActionLog entry)
    {
        _actions.Add(entry);
        return _actions.Count;
    }

    public ScenarioRunRecord ToRecord(DateTimeOffset? endedAt, IReadOnlyList<string> observed, ScenarioOutcome outcome)
    {
        var measurements = new Dictionary<string, double>(Measurements.ToDictionary(), StringComparer.Ordinal)
        {
            ["actionsExecuted"] = _actions.Count(a => a.Succeeded),
            ["actionsFailed"] = _actions.Count(a => !a.Succeeded)
        };
        var duration = TimeSpan.FromSeconds(Definition.DurationSeconds);
        var elapsed = TimeSpan.FromSeconds(Math.Min(ElapsedSeconds, Definition.DurationSeconds));
        return new ScenarioRunRecord(RunId, Definition.Id, Definition.Name, Definition.Category, State, startedBy, startedAt,
            endedAt, simulationStart, elapsed, duration, Math.Round(elapsed / duration * 100, 1), _actions.ToArray(),
            measurements, Definition.ExpectedDetections, observed, outcome, replayOf);
    }
}
