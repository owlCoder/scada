namespace Domain.Incidents;

/// <summary>Pure incident state transitions. Persistence and concurrency are handled by the application layer.</summary>
public static class IncidentWorkflow
{
    public const int MaxTextLength = 4000;

    public static IncidentChange ChangeStatus(Incident incident, IncidentStatus target, string actor, string? notes, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(incident);
        if (string.IsNullOrWhiteSpace(actor))
            return IncidentChange.Failure(IncidentOperationStatus.InvalidInput, "An authenticated actor is required.");
        if (!Enum.IsDefined(target))
            return IncidentChange.Failure(IncidentOperationStatus.InvalidInput, "Unknown incident status.");
        if (!IncidentStateMachine.CanTransition(incident.Status, target))
            return IncidentChange.Failure(IncidentOperationStatus.InvalidTransition,
                $"Cannot move incident from {incident.Status} to {target}.");
        if (target == IncidentStatus.Resolved && string.IsNullOrWhiteSpace(notes))
            return IncidentChange.Failure(IncidentOperationStatus.InvalidInput, "Resolution notes are required.");
        if (notes?.Length > MaxTextLength)
            return IncidentChange.Failure(IncidentOperationStatus.InvalidInput, "Notes are too long.");

        var updated = incident with
        {
            Status = target,
            UpdatedAt = now,
            AcknowledgedBy = target == IncidentStatus.Acknowledged ? actor : incident.AcknowledgedBy,
            AcknowledgedAt = target == IncidentStatus.Acknowledged ? now : incident.AcknowledgedAt,
            ResolvedBy = target == IncidentStatus.Resolved ? actor : target == IncidentStatus.Open ? null : incident.ResolvedBy,
            ResolvedAt = target == IncidentStatus.Resolved ? now : target == IncidentStatus.Open ? null : incident.ResolvedAt,
            ResolutionNotes = target == IncidentStatus.Resolved ? notes!.Trim() : incident.ResolutionNotes,
            AssignedTo = target == IncidentStatus.InProgress && incident.AssignedTo is null ? actor : incident.AssignedTo
        };

        var description = target == IncidentStatus.Open
            ? $"Incident reopened{FormatNotes(notes)}"
            : $"Status changed from {incident.Status} to {target}{FormatNotes(notes)}";
        var entry = new IncidentTimelineEntry(Guid.NewGuid(), incident.Id, now, actor,
            IncidentTimelineEntryKind.StatusChanged, description, incident.Status, target);
        return new IncidentChange(IncidentOperationStatus.Succeeded, updated, [entry], description);
    }

    public static IncidentChange Assign(Incident incident, string? assignee, string actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(incident);
        if (string.IsNullOrWhiteSpace(actor))
            return IncidentChange.Failure(IncidentOperationStatus.InvalidInput, "An authenticated actor is required.");
        if (incident.Status == IncidentStatus.Closed)
            return IncidentChange.Failure(IncidentOperationStatus.InvalidTransition, "Closed incidents cannot be reassigned.");
        if (assignee is { Length: > 100 })
            return IncidentChange.Failure(IncidentOperationStatus.InvalidInput, "Assignee name is too long.");

        var normalized = string.IsNullOrWhiteSpace(assignee) ? null : assignee.Trim();
        var updated = incident with { AssignedTo = normalized, UpdatedAt = now };
        var description = normalized is null ? "Assignment cleared" : $"Assigned to {normalized}";
        return new IncidentChange(IncidentOperationStatus.Succeeded, updated,
            [new IncidentTimelineEntry(Guid.NewGuid(), incident.Id, now, actor, IncidentTimelineEntryKind.Assigned, description, null, null)],
            description);
    }

    public static IncidentChange ChangePriority(Incident incident, IncidentPriority priority, string actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(incident);
        if (!Enum.IsDefined(priority) || string.IsNullOrWhiteSpace(actor))
            return IncidentChange.Failure(IncidentOperationStatus.InvalidInput, "A valid priority and actor are required.");
        if (incident.Status == IncidentStatus.Closed)
            return IncidentChange.Failure(IncidentOperationStatus.InvalidTransition, "Closed incidents cannot be changed.");

        var updated = incident with { Priority = priority, UpdatedAt = now };
        var description = $"Priority changed from {incident.Priority} to {priority}";
        return new IncidentChange(IncidentOperationStatus.Succeeded, updated,
            [new IncidentTimelineEntry(Guid.NewGuid(), incident.Id, now, actor, IncidentTimelineEntryKind.PriorityChanged, description, null, null)],
            description);
    }

    public static IncidentChange AddEvidence(Incident incident, IncidentEvidence evidence, string actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(evidence);
        if (incident.Evidence.Any(e => e.Kind == evidence.Kind && e.Reference == evidence.Reference))
            return new IncidentChange(IncidentOperationStatus.Succeeded, incident, [], "Evidence already attached");

        var updated = incident with { Evidence = [.. incident.Evidence, evidence], UpdatedAt = now };
        return new IncidentChange(IncidentOperationStatus.Succeeded, updated,
            [new IncidentTimelineEntry(Guid.NewGuid(), incident.Id, now, actor, IncidentTimelineEntryKind.EvidenceAdded,
                $"Evidence added: {evidence.Summary}", null, null)],
            "Evidence added");
    }

    public static IncidentPriority PriorityFor(IncidentSeverity severity) => severity switch
    {
        IncidentSeverity.Critical => IncidentPriority.Urgent,
        IncidentSeverity.High => IncidentPriority.High,
        IncidentSeverity.Medium => IncidentPriority.Normal,
        _ => IncidentPriority.Low
    };

    private static string FormatNotes(string? notes) => string.IsNullOrWhiteSpace(notes) ? "" : $": {notes.Trim()}";
}
