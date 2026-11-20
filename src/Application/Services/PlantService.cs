using Domain;

namespace Application;

public sealed class PlantService(Plant plant, IEventStore events, ITelemetryStore telemetry) : IPlantService
{
    private readonly object _sync = new();
    private AlarmLevel _lastAlarm = AlarmLevel.Normal;

    public PlantState GetState() => plant.GetState();

    public PlantState Advance(double seconds)
    {
        lock (_sync)
        {
            var previous = plant.GetState();
            var state = plant.Advance(seconds);
            if (previous.PumpRunning && !state.PumpRunning && state.TankLevel >= state.TankCapacity)
                events.Append(new(DateTimeOffset.UtcNow, "SafetyInterlock", "Pump automatically stopped because the tank is full", "simulation"));
            telemetry.Append(new(DateTimeOffset.UtcNow, state.TankLevel, state.PumpRunning, state.Revision));

            var alarm = state.TankLevel >= 90 ? AlarmLevel.High
                : state.TankLevel <= 10 ? AlarmLevel.Low
                : AlarmLevel.Normal;

            if (alarm == _lastAlarm)
                return state;

            var message = alarm switch
            {
                AlarmLevel.High => "High tank level",
                AlarmLevel.Low => "Low tank level",
                _ => "Tank level returned to normal"
            };

            events.Append(new(DateTimeOffset.UtcNow, "ProcessAlarm", message, "simulation"));
            _lastAlarm = alarm;
            return state;
        }
    }

    public PlantState SetPump(bool running, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Operator is required.", nameof(actor));

        lock (_sync)
        {
            var state = plant.SetPump(running);
            events.Append(new(DateTimeOffset.UtcNow, "Audit", running ? "Pump started" : "Pump stopped", actor));
            return state;
        }
    }

    public PlantState SetOutletValve(bool open, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Operator is required.", nameof(actor));

        lock (_sync)
        {
            var state = plant.SetOutletValve(open);
            events.Append(new(DateTimeOffset.UtcNow, "Audit",
                open ? "Outlet valve opened" : "Outlet valve closed", actor));
            return state;
        }
    }
}
