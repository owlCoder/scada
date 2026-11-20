using System.Security.Cryptography;
using System.Text;

namespace Application;

public sealed class IncidentService(IIncidentStore store)
{
    private readonly object _sync = new();

    public IReadOnlyList<Incident> GetAll() => store.GetAll();

    public int Observe(IEnumerable<SecurityEvent> events)
    {
        lock (_sync)
        {
            var created = 0;
            foreach (var item in events.Where(x => x.Category is "SecurityAlert" or "SafetyInterlock"))
            {
                var fingerprint = $"{item.Timestamp.UtcTicks}:{item.Category}:{item.Actor}:{item.Message}";
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
                var id = new Guid(hash.AsSpan(0, 16));
                if (store.TryAdd(new Incident(id, item.Timestamp, item.Category,
                    item.Message, item.Actor, IncidentStatus.Open, null, null)))
                    created++;
            }
            return created;
        }
    }

    public bool TryUpdate(Guid id, IncidentStatus status, string actor, out Incident? incident)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Operator is required.", nameof(actor));

        lock (_sync)
        {
            var existing = store.GetAll().FirstOrDefault(x => x.Id == id);
            if (existing is null)
            {
                incident = null;
                return false;
            }

            if (status < existing.Status)
                throw new InvalidOperationException("Incident status cannot move backwards.");

            incident = existing with
            {
                Status = status,
                UpdatedBy = actor,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            if (store.TryUpdate(incident, existing.Status))
                return true;

            incident = null;
            return false;
        }
    }
}
