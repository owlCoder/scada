using Domain;

namespace Application;

public sealed class PlantService(Plant plant, IEventStore events, ITelemetryStore telemetry) : IPlantService
{
    private readonly object _sync = new();
    private AlarmLevel _lastAlarm = AlarmLevel.Normal;

    public PlantState GetState() => plant.GetState();

    public void Advance(double seconds)
    {
        lock (_sync)
        {
            var previous = plant.GetState();
            plant.Advance(seconds);
            var state = plant.GetState();
            if (previous.PumpRunning && !state.PumpRunning && state.TankLevel >= state.TankCapacity)
                events.Append(new(DateTimeOffset.UtcNow, "SafetyInterlock", "Пумпа аутоматски искључена због пуног резервоара", "simulation"));
            telemetry.Append(new(DateTimeOffset.UtcNow, state.TankLevel, state.PumpRunning, state.Revision));

            var alarm = state.TankLevel >= 90 ? AlarmLevel.High
                : state.TankLevel <= 10 ? AlarmLevel.Low
                : AlarmLevel.Normal;

            if (alarm == _lastAlarm)
                return;

            var message = alarm switch
            {
                AlarmLevel.High => "Висок ниво резервоара",
                AlarmLevel.Low => "Низак ниво резервоара",
                _ => "Ниво резервоара је нормализован"
            };

            events.Append(new(DateTimeOffset.UtcNow, "ProcessAlarm", message, "simulation"));
            _lastAlarm = alarm;
        }
    }

    public void SetPump(bool running, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Оператер је обавезан.", nameof(actor));

        lock (_sync)
        {
            plant.SetPump(running);
            events.Append(new(DateTimeOffset.UtcNow, "Audit", running ? "Пумпа укључена" : "Пумпа искључена", actor));
        }
    }

    public void SetOutletValve(bool open, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Оператер је обавезан.", nameof(actor));

        lock (_sync)
        {
            plant.SetOutletValve(open);
            events.Append(new(DateTimeOffset.UtcNow, "Audit",
                open ? "Излазни вентил отворен" : "Излазни вентил затворен", actor));
        }
    }

    private enum AlarmLevel { Normal, Low, High }
}
