using Application.Telemetry;

namespace Application.Persistence;

public sealed record TelemetryBatchWorkItem(IReadOnlyList<TelemetrySample> Samples) : PersistenceWorkItem("telemetry");
