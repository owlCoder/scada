using Application.Alarms;
using Application.Events;
using Application.Persistence;
using Application.Simulation;
using Application.Telemetry;

namespace Infrastructure.Persistence.Queue;

/// <summary>Writes a batch of work items to the configured stores, grouped by kind.</summary>
public sealed class PersistenceDispatcher(
    IEventStore events,
    ITelemetryStore telemetry,
    IAlarmHistoryStore alarms,
    IPlantStateStore plantState)
{
    public async Task<int> DispatchAsync(IReadOnlyList<PersistenceWorkItem> batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var written = 0;
        var securityEvents = batch.OfType<SecurityEventWorkItem>().Select(i => i.Event).ToArray();
        if (securityEvents.Length > 0)
            written += await events.AppendAsync(securityEvents, cancellationToken);

        var samples = batch.OfType<TelemetryBatchWorkItem>().SelectMany(i => i.Samples).ToArray();
        if (samples.Length > 0)
            written += await telemetry.AppendAsync(samples, cancellationToken);

        var alarmRecords = batch.OfType<AlarmHistoryWorkItem>().Select(i => i.Record).ToArray();
        if (alarmRecords.Length > 0)
            written += await alarms.AppendAsync(alarmRecords, cancellationToken);

        // Only the newest plant state matters; older snapshots in the same batch are superseded.
        if (batch.OfType<SimulationStateWorkItem>().MaxBy(i => i.State.SavedAt) is { } state)
            written += await plantState.SaveAsync(state.State, cancellationToken) ? 1 : 0;

        return written;
    }
}
