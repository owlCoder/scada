namespace Api.Contracts;

public sealed record IncidentAssignRequest(string? Assignee, int? ExpectedVersion = null);
