namespace Security.Grid.Scenarios;

/// <summary>One target selector used by a scenario action. <see cref="Count"/> selects how many ids are resolved (default 1).</summary>
public sealed record GridScenarioTarget(GridScenarioTargetKind Kind, int Count = 1)
{
    public string Describe() => Count > 1 ? $"{Kind} x{Count}" : Kind.ToString();
}
