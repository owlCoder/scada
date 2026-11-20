using Domain;

namespace Application;

public interface IPlantService
{
    PlantState GetState();
    void Advance(double seconds);
    void SetPump(bool running, string actor);
}

public interface IEventStore
{
    void Append(SecurityEvent securityEvent);
    IReadOnlyList<SecurityEvent> GetRecent();
}

public sealed record SecurityEvent(DateTimeOffset Timestamp, string Category, string Message, string Actor);

public sealed class PlantService(Plant plant, IEventStore events) : IPlantService
{
    public PlantState GetState() => plant.GetState();

    public void Advance(double seconds)
    {
        plant.Advance(seconds);
        var state = plant.GetState();
        if (state.TankLevel >= 90)
            events.Append(new(DateTimeOffset.UtcNow, "ProcessAlarm", "Висок ниво резервоара", "simulation"));
        if (state.TankLevel <= 10)
            events.Append(new(DateTimeOffset.UtcNow, "ProcessAlarm", "Низак ниво резервоара", "simulation"));
    }

    public void SetPump(bool running, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Оператер је обавезан.", nameof(actor));

        plant.SetPump(running);
        events.Append(new(DateTimeOffset.UtcNow, "Audit", running ? "Пумпа укључена" : "Пумпа искључена", actor));
    }
}
