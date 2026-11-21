namespace Security.Grid.Scenarios;

/// <summary>The ids resolved for every target of a scenario at run start, keyed by the target selector.</summary>
public sealed class GridScenarioTargets
{
    private readonly Dictionary<GridScenarioTarget, string[]> _resolved;

    public GridScenarioTargets(Dictionary<GridScenarioTarget, string[]> resolved)
    {
        _resolved = new Dictionary<GridScenarioTarget, string[]>(resolved);
    }

    public IReadOnlyList<string> Resolve(GridScenarioTarget target) =>
        _resolved.TryGetValue(target, out var ids) ? ids : [];

    /// <summary>Resolves one id; throws a clear error when the scenario's target could not be found on this network.</summary>
    public string ResolveOne(GridScenarioTarget target, int index)
    {
        var ids = Resolve(target);
        if (index < 0 || index >= ids.Count)
            throw new InvalidOperationException(
                $"Scenario target {target.Describe()} has no element at index {index} (resolved: {string.Join(", ", ids) ?? "none"}).");
        return ids[index];
    }
}
