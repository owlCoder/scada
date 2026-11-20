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

public sealed class IncidentService
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Incident> _incidents = new();
    private readonly HashSet<string> _observed = new(StringComparer.Ordinal);
    private readonly Queue<string> _observationOrder = new();
    private const int Capacity = 1000;

    public IReadOnlyList<Incident> GetAll()
    {
        lock (_sync)
            return _incidents.Values.OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public void Observe(IEnumerable<SecurityEvent> events)
    {
        lock (_sync)
        {
            foreach (var item in events.Where(x => x.Category is "SecurityAlert" or "SafetyInterlock"))
            {
                var key = $"{item.Timestamp.UtcTicks}:{item.Category}:{item.Actor}:{item.Message}";
                if (!_observed.Add(key))
                    continue;

                _observationOrder.Enqueue(key);
                while (_observationOrder.Count > 10000)
                    _observed.Remove(_observationOrder.Dequeue());

                var incident = new Incident(Guid.NewGuid(), item.Timestamp, item.Category,
                    item.Message, item.Actor, IncidentStatus.Open, null, null);
                _incidents.Add(incident.Id, incident);
            }

            if (_incidents.Count <= Capacity)
                return;

            foreach (var incident in _incidents.Values.OrderBy(x => x.CreatedAt)
                .Take(_incidents.Count - Capacity).ToArray())
                _incidents.Remove(incident.Id);
        }
    }

    public bool TryUpdate(Guid id, IncidentStatus status, string actor, out Incident? incident)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Оператер је обавезан.", nameof(actor));

        lock (_sync)
        {
            if (!_incidents.TryGetValue(id, out var existing))
            {
                incident = null;
                return false;
            }

            if (status < existing.Status)
                throw new InvalidOperationException("Статус инцидента не може се вратити уназад.");

            incident = existing with
            {
                Status = status,
                UpdatedBy = actor,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _incidents[id] = incident;
            return true;
        }
    }
}
