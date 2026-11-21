using Domain.Incidents;

namespace Api.Contracts;

public sealed record IncidentStatusRequest(IncidentStatus Status, string? Notes = null, int? ExpectedVersion = null);
