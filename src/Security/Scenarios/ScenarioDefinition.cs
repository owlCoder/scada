namespace Security.Scenarios;

/// <summary>
/// A scripted training scenario. Times are simulated seconds since the run started. <see cref="Cleanup"/>
/// actions run when the run ends for any reason (completion, stop or reset), before training defaults are restored.
/// </summary>
public sealed record ScenarioDefinition(
    string Id,
    string Name,
    string Category,
    string Description,
    double DurationSeconds,
    IReadOnlyList<string> ExpectedDetections,
    IReadOnlyList<ScheduledScenarioAction> Actions,
    IReadOnlyList<IScenarioAction>? Cleanup = null)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Id))
            errors.Add("Scenario id is required.");
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add($"Scenario {Id}: name is required.");
        if (!double.IsFinite(DurationSeconds) || DurationSeconds is <= 0 or > 86_400)
            errors.Add($"Scenario {Id}: duration must be between 0 and 86400 simulated seconds.");
        if (Actions.Count == 0)
            errors.Add($"Scenario {Id}: at least one action is required.");
        foreach (var action in Actions.Where(a => a.Action is null || !double.IsFinite(a.AtSeconds) || a.AtSeconds < 0 || a.AtSeconds >= DurationSeconds))
            errors.Add($"Scenario {Id}: action at {action.AtSeconds}s must be inside the run duration.");
        if (ExpectedDetections.Any(string.IsNullOrWhiteSpace))
            errors.Add($"Scenario {Id}: expected detection ids must not be empty.");
        return errors;
    }
}
