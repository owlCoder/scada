namespace Domain.Incidents;

public enum IncidentOperationStatus { Succeeded, NotFound, InvalidTransition, InvalidInput, ConcurrencyConflict }
