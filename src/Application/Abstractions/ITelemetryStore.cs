namespace Application;

public interface ITelemetryStore
{
    bool Append(TelemetrySample sample);
    IReadOnlyList<TelemetrySample> GetRecent(int count);
}
