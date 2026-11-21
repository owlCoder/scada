using Domain.Incidents;

namespace Api.Contracts;

public sealed record IncidentPriorityRequest(IncidentPriority Priority, int? ExpectedVersion = null);
