using Application;

namespace Api.Contracts;

public sealed record IncidentStatusRequest(IncidentStatus Status, string? Actor = null);
