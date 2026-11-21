namespace Security.Detection;

public sealed record RuleExecutionMetrics(
    string RuleId,
    string Name,
    bool Enabled,
    long Evaluations,
    long Findings,
    long Suppressed,
    double TotalMilliseconds,
    DateTimeOffset? LastFindingAt);
