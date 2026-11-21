using Application.Common;
using Application.Incidents;
using Domain.Incidents;

namespace Infrastructure.Persistence.InMemory;

/// <summary>Incident store with sequential numbering, timelines, comments and optimistic version checks.</summary>
public sealed class InMemoryIncidentStore : IIncidentStore
{
    private readonly Lock _sync = new();
    private readonly Dictionary<Guid, Incident> _incidents = [];
    private readonly Dictionary<Guid, List<IncidentTimelineEntry>> _timelines = [];
    private readonly Dictionary<Guid, List<IncidentComment>> _comments = [];
    private long _lastNumber;

    public Task<Incident?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        lock (_sync)
            return Task.FromResult(_incidents.GetValueOrDefault(id));
    }

    public Task<Incident?> FindActiveByCorrelationAsync(string correlationKey, CancellationToken cancellationToken)
    {
        lock (_sync)
            return Task.FromResult(_incidents.Values
                .Where(i => i.IsActive && string.Equals(i.CorrelationKey, correlationKey, StringComparison.Ordinal))
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefault());
    }

    public Task<PagedResult<Incident>> QueryAsync(IncidentQuery query, CancellationToken cancellationToken)
    {
        Incident[] snapshot;
        lock (_sync)
            snapshot = _incidents.Values.ToArray();
        return Task.FromResult(IncidentQueryEvaluator.Apply(snapshot, query));
    }

    public Task<Incident> AddAsync(Incident incident, IReadOnlyList<IncidentTimelineEntry> timeline, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);
        lock (_sync)
        {
            if (_incidents.ContainsKey(incident.Id))
                throw new InvalidOperationException($"Incident {incident.Id} already exists.");

            var stored = incident with { Number = ++_lastNumber };
            _incidents[stored.Id] = stored;
            _timelines[stored.Id] = [.. timeline];
            _comments[stored.Id] = [];
            return Task.FromResult(stored);
        }
    }

    public Task<bool> UpdateAsync(Incident incident, int expectedVersion, IReadOnlyList<IncidentTimelineEntry> timeline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);
        lock (_sync)
        {
            if (!_incidents.TryGetValue(incident.Id, out var current) || current.Version != expectedVersion)
                return Task.FromResult(false);

            _incidents[incident.Id] = incident with { Number = current.Number };
            _timelines[incident.Id].AddRange(timeline);
            return Task.FromResult(true);
        }
    }

    public Task<bool> AddCommentAsync(IncidentComment comment, IncidentTimelineEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comment);
        lock (_sync)
        {
            if (!_incidents.ContainsKey(comment.IncidentId))
                return Task.FromResult(false);

            _comments[comment.IncidentId].Add(comment);
            _timelines[comment.IncidentId].Add(entry);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<IncidentComment>> GetCommentsAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        lock (_sync)
            return Task.FromResult<IReadOnlyList<IncidentComment>>(
                _comments.TryGetValue(incidentId, out var comments) ? comments.ToArray() : []);
    }

    public Task<IReadOnlyList<IncidentTimelineEntry>> GetTimelineAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        lock (_sync)
            return Task.FromResult<IReadOnlyList<IncidentTimelineEntry>>(
                _timelines.TryGetValue(incidentId, out var timeline) ? timeline.OrderBy(e => e.Timestamp).ToArray() : []);
    }
}
