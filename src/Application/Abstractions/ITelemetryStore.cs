namespace Application;

public interface ITelemetryStore
{
    void Append(TelemetrySample sample);
    IReadOnlyList<TelemetrySample> GetRecent(int count);
}
