namespace Security.Grid.Scenarios;

/// <summary>Outcome of resolving all targets of a scenario against a network.</summary>
public sealed record GridScenarioTargetResolution(GridScenarioTargets? Targets, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Targets is not null && Errors.Count == 0;

    public static GridScenarioTargetResolution Ok(GridScenarioTargets targets) => new(targets, []);

    public static GridScenarioTargetResolution Failed(IReadOnlyList<string> errors) => new(null, errors);
}
