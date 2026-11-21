namespace Application.Scenarios;

public sealed record ScenarioActionLog(TimeSpan Offset, DateTimeOffset ExecutedAt, string Action, bool Succeeded, string Detail);
