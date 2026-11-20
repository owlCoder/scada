using Domain;

namespace Application;

public interface IPlantService
{
    PlantState GetState();
    void Advance(double seconds);
    void SetPump(bool running, string actor);
    void SetOutletValve(bool open, string actor);
}

public interface IEventStore
{
    void Append(SecurityEvent securityEvent);
    IReadOnlyList<SecurityEvent> GetRecent();
}

public interface ITelemetryStore
{
    void Append(TelemetrySample sample);
    IReadOnlyList<TelemetrySample> GetRecent(int count);
}

public sealed record SecurityEvent(DateTimeOffset Timestamp, string Category, string Message, string Actor);
public sealed record TelemetrySample(DateTimeOffset Timestamp, double TankLevel, bool PumpRunning, long Revision);
