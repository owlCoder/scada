namespace Domain.Incidents;

public sealed record IncidentEvidence(DateTimeOffset Timestamp, string Kind, string Reference, string Summary);
