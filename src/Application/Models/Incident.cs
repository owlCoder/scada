namespace Application;

public sealed record Incident(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Category,
    string Description,
    string Actor,
    IncidentStatus Status,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAt);
