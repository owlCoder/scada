using System.Security.Cryptography;
using System.Text;

namespace Application;

public enum IncidentStatus { Open, Acknowledged, Resolved }

public sealed record Incident(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Category,
    string Description,
    string Actor,
    IncidentStatus Status,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAt);
