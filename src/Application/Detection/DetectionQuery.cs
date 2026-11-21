namespace Application.Detection;

public sealed record DetectionQuery(string? RuleId = null, Guid? ScenarioRunId = null, DateTimeOffset? Since = null, int Limit = 200);
