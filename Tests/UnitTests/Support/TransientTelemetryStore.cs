using Application.Telemetry;
using Infrastructure.Persistence.InMemory;

namespace UnitTests.Support;

public sealed class TransientTelemetryStore : ITelemetryStore
{
    private readonly InMemoryTelemetryStore _store = new();

    public int Attempts { get; private set; }

    public Task<int> AppendAsync(IReadOnlyList<TelemetrySample> samples, CancellationToken cancellationToken) =>
        ++Attempts == 1
            ? throw new InvalidOperationException("temporary telemetry failure")
            : _store.AppendAsync(samples, cancellationToken);

    public Task<IReadOnlyList<TelemetrySample>> QueryAsync(TelemetryQuery query, CancellationToken cancellationToken) =>
        _store.QueryAsync(query, cancellationToken);
}
