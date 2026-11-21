namespace Application.Telemetry;

public interface ITelemetryStore
{
    Task<int> AppendAsync(IReadOnlyList<TelemetrySample> samples, CancellationToken cancellationToken);
    Task<IReadOnlyList<TelemetrySample>> QueryAsync(TelemetryQuery query, CancellationToken cancellationToken);
}
