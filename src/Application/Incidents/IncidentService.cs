using Application.Common;
using Application.Events;
using Application.Identity;
using Application.Realtime;
using Domain.Incidents;

namespace Application.Incidents;

public sealed class IncidentService(
    IIncidentStore store,
    IEventPublisher events,
    IRealtimePublisher realtime,
    TimeProvider time)
{
    private readonly SemaphoreSlim _raiseGate = new(1, 1);

    public Task<PagedResult<Incident>> QueryAsync(IncidentQuery query, CancellationToken cancellationToken) =>
        store.QueryAsync(query ?? new IncidentQuery(), cancellationToken);

    public Task<Incident?> GetAsync(Guid id, CancellationToken cancellationToken) => store.GetAsync(id, cancellationToken);

    public async Task<IncidentDetails?> GetDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        var incident = await store.GetAsync(id, cancellationToken);
        if (incident is null)
            return null;

        var comments = await store.GetCommentsAsync(id, cancellationToken);
        var timeline = await store.GetTimelineAsync(id, cancellationToken);
        return new IncidentDetails(incident, comments, timeline, IncidentStateMachine.AllowedTransitions(incident.Status));
    }

    /// <summary>
    /// Creates an incident or, when an active incident shares the correlation key, attaches the new evidence to it.
    /// </summary>
    public async Task<IncidentChange> RaiseAsync(IncidentDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.Title) || string.IsNullOrWhiteSpace(draft.CorrelationKey))
            return IncidentChange.Failure(IncidentOperationStatus.InvalidInput, "Title and correlation key are required.");

        await _raiseGate.WaitAsync(cancellationToken);
        try
        {
            var now = time.GetUtcNow();
            var existing = await store.FindActiveByCorrelationAsync(draft.CorrelationKey, cancellationToken);
            if (existing is not null)
            {
                var merged = IncidentWorkflow.AddEvidence(existing, draft.Evidence, "detection-engine", now);
                if (merged.Timeline.Count == 0)
                    return merged;

                var escalated = merged.Incident! with
                {
                    Severity = draft.Severity > existing.Severity ? draft.Severity : existing.Severity,
                    DeviceIds = existing.DeviceIds.Union(draft.DeviceIds, StringComparer.Ordinal).ToArray()
                };
                return await PersistAsync(existing, merged with { Incident = escalated }, cancellationToken);
            }

            var incident = new Incident(Guid.NewGuid(), 0, Truncate(draft.Title, 200), Truncate(draft.Description, IncidentWorkflow.MaxTextLength),
                draft.Category, draft.Severity, IncidentWorkflow.PriorityFor(draft.Severity), draft.Source, IncidentStatus.Open,
                null, now, now, null, null, null, null, null, draft.DeviceIds.Distinct(StringComparer.Ordinal).ToArray(),
                draft.ScenarioRunId, draft.CorrelationKey, [draft.Evidence], 1);
            var created = await store.AddAsync(incident,
                [new IncidentTimelineEntry(Guid.NewGuid(), incident.Id, now, draft.Source, IncidentTimelineEntryKind.Created,
                    $"Incident created: {draft.Title}", null, IncidentStatus.Open)], cancellationToken);
            events.Publish(SecurityEvent.Create(now, EventCategories.SecurityAlert, Map(created.Severity), draft.Source,
                $"Incident {created.DisplayId} opened: {created.Title}", draft.Source,
                created.DeviceIds.FirstOrDefault(), created.ScenarioRunId));
            realtime.Publish(RealtimeTopics.Incidents, created);
            return new IncidentChange(IncidentOperationStatus.Succeeded, created, [], "Incident created");
        }
        finally
        {
            _raiseGate.Release();
        }
    }

    public Task<IncidentChange> ChangeStatusAsync(Guid id, IncidentStatus status, string? notes, int? expectedVersion,
        Actor actor, CancellationToken cancellationToken) =>
        UpdateAsync(id, expectedVersion, actor, AccessRole.Operator,
            incident => IncidentWorkflow.ChangeStatus(incident, status, actor.Name, notes, time.GetUtcNow()), cancellationToken);

    public Task<IncidentChange> AssignAsync(Guid id, string? assignee, int? expectedVersion, Actor actor, CancellationToken cancellationToken) =>
        UpdateAsync(id, expectedVersion, actor, AccessRole.Operator,
            incident => IncidentWorkflow.Assign(incident, assignee, actor.Name, time.GetUtcNow()), cancellationToken);

    public Task<IncidentChange> ChangePriorityAsync(Guid id, IncidentPriority priority, int? expectedVersion, Actor actor,
        CancellationToken cancellationToken) =>
        UpdateAsync(id, expectedVersion, actor, AccessRole.Operator,
            incident => IncidentWorkflow.ChangePriority(incident, priority, actor.Name, time.GetUtcNow()), cancellationToken);

    public async Task<IncidentChange> AddCommentAsync(Guid id, string text, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.HasRole(AccessRole.Operator))
            return IncidentChange.Failure(IncidentOperationStatus.InvalidInput, "Operator role required.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > IncidentWorkflow.MaxTextLength)
            return IncidentChange.Failure(IncidentOperationStatus.InvalidInput, "Comment must contain 1 to 4000 characters.");

        var incident = await store.GetAsync(id, cancellationToken);
        if (incident is null)
            return IncidentChange.Failure(IncidentOperationStatus.NotFound, "Incident not found.");
        if (incident.Status == IncidentStatus.Closed)
            return IncidentChange.Failure(IncidentOperationStatus.InvalidTransition, "Closed incidents cannot receive comments.");

        var now = time.GetUtcNow();
        var comment = new IncidentComment(Guid.NewGuid(), id, actor.Name, text.Trim(), now);
        var entry = new IncidentTimelineEntry(Guid.NewGuid(), id, now, actor.Name, IncidentTimelineEntryKind.Commented,
            "Comment added", null, null);
        if (!await store.AddCommentAsync(comment, entry, cancellationToken))
            return IncidentChange.Failure(IncidentOperationStatus.NotFound, "Incident not found.");

        events.Publish(SecurityEvent.Create(now, EventCategories.Audit, EventSeverity.Information, "incidents",
            $"Comment added to {incident.DisplayId}", actor.Name));
        return new IncidentChange(IncidentOperationStatus.Succeeded, incident, [entry], "Comment added");
    }

    private async Task<IncidentChange> UpdateAsync(Guid id, int? expectedVersion, Actor actor, AccessRole required,
        Func<Incident, IncidentChange> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.HasRole(required))
            return IncidentChange.Failure(IncidentOperationStatus.InvalidInput, $"{required} role required.");

        var incident = await store.GetAsync(id, cancellationToken);
        if (incident is null)
            return IncidentChange.Failure(IncidentOperationStatus.NotFound, "Incident not found.");
        if (expectedVersion is { } version && version != incident.Version)
            return IncidentChange.Failure(IncidentOperationStatus.ConcurrencyConflict,
                $"Incident was modified (expected version {version}, current {incident.Version}).");

        var result = change(incident);
        if (!result.Succeeded)
            return result;

        var persisted = await PersistAsync(incident, result, cancellationToken);
        if (persisted.Succeeded)
            events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.Audit, EventSeverity.Information,
                "incidents", $"{incident.DisplayId}: {result.Message}", actor.Name));
        return persisted;
    }

    private async Task<IncidentChange> PersistAsync(Incident original, IncidentChange change, CancellationToken cancellationToken)
    {
        var updated = change.Incident! with { Version = original.Version + 1 };
        if (!await store.UpdateAsync(updated, original.Version, change.Timeline, cancellationToken))
            return IncidentChange.Failure(IncidentOperationStatus.ConcurrencyConflict, "Incident was modified concurrently.");

        realtime.Publish(RealtimeTopics.Incidents, updated);
        return change with { Incident = updated };
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private static EventSeverity Map(IncidentSeverity severity) => severity switch
    {
        IncidentSeverity.Critical => EventSeverity.Critical,
        IncidentSeverity.High => EventSeverity.High,
        IncidentSeverity.Medium => EventSeverity.Medium,
        _ => EventSeverity.Low
    };
}
