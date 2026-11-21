using Application.Alarms;
using Application.Events;
using Application.Persistence;
using Application.Realtime;
using Application.Simulation;
using Application.Telemetry;

namespace Infrastructure.Persistence.Queue;

/// <summary>Writes a group of work items of the same kind to its configured store.</summary>
public sealed class PersistenceDispatcher(
    IEventStore events,
    ITelemetryStore telemetry,
    IAlarmHistoryStore alarms,
    IPlantStateStore plantState,
    IRealtimePublisher? realtime = null)
{
    public async Task<int> DispatchAsync(IReadOnlyList<PersistenceWorkItem> batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        switch (batch.FirstOrDefault())
        {
            case SecurityEventWorkItem:
                var written = await events.AppendAsync(batch.Cast<SecurityEventWorkItem>().Select(i => i.Event).ToArray(),
                    cancellationToken);
                realtime?.Publish(RealtimeTopics.EventsPersisted, written);
                return written;
            case TelemetryBatchWorkItem:
                return await telemetry.AppendAsync(batch.Cast<TelemetryBatchWorkItem>().SelectMany(i => i.Samples).ToArray(),
                    cancellationToken);
            case AlarmHistoryWorkItem:
                return await alarms.AppendAsync(batch.Cast<AlarmHistoryWorkItem>().Select(i => i.Record).ToArray(),
                    cancellationToken);
            case SimulationStateWorkItem:
                // Older snapshots in the same group are superseded.
                return await plantState.SaveAsync(
                    batch.Cast<SimulationStateWorkItem>().MaxBy(i => i.State.SavedAt)!.State, cancellationToken) ? 1 : 0;
            case null:
                return 0;
            case var item:
                throw new InvalidOperationException($"Unsupported persistence work item kind: {item.Kind}");
        }
    }
}
