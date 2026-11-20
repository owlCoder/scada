namespace Application;

public interface IIncidentStore
{
    IReadOnlyList<Incident> GetAll();
    bool TryAdd(Incident incident);
    bool TryUpdate(Incident incident, IncidentStatus expectedStatus);
}
