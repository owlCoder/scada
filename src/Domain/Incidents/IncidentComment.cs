namespace Domain.Incidents;

public sealed record IncidentComment(Guid Id, Guid IncidentId, string Author, string Text, DateTimeOffset CreatedAt);
