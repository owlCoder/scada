using Application;

namespace Infrastructure;

public sealed class InMemoryIncidentStore : IIncidentStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Incident> _incidents = new();

    public IReadOnlyList<Incident> GetAll()
    {
        lock (_sync)
            return _incidents.Values.OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public bool TryAdd(Incident incident)
    {
        lock (_sync)
            return _incidents.TryAdd(incident.Id, incident);
    }

    public bool TryUpdate(Incident incident, IncidentStatus expectedStatus)
    {
        lock (_sync)
        {
            if (!_incidents.TryGetValue(incident.Id, out var current) ||
                current.Status != expectedStatus)
                return false;

            _incidents[incident.Id] = incident;
            return true;
        }
    }
}
