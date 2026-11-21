using Application.Alarms;
using Application.Events;
using Application.Persistence;
using Application.Simulation;
using Application.Telemetry;

namespace Infrastructure.Persistence.Queue;

/// <summary>Writes a group of work items of the same kind to its configured store.</summary>
public sealed class PersistenceDispatcher(
    IEventStore events,
    ITelemetryStore telemetry,
    IAlarmHistoryStore alarms,
    IPlantStateStore plantState)
{
    public async Task<int> DispatchAsync(IReadOnlyList<PersistenceWorkItem> batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return batch.FirstOrDefault() switch
        {
            SecurityEventWorkItem => await events.AppendAsync(batch.Cast<SecurityEventWorkItem>().Select(i => i.Event).ToArray(),
                cancellationToken),
            TelemetryBatchWorkItem => await telemetry.AppendAsync(batch.Cast<TelemetryBatchWorkItem>().SelectMany(i => i.Samples).ToArray(),
                cancellationToken),
            AlarmHistoryWorkItem => await alarms.AppendAsync(batch.Cast<AlarmHistoryWorkItem>().Select(i => i.Record).ToArray(),
                cancellationToken),
            // Older snapshots in the same group are superseded.
            SimulationStateWorkItem => await plantState.SaveAsync(
                batch.Cast<SimulationStateWorkItem>().MaxBy(i => i.State.SavedAt)!.State, cancellationToken) ? 1 : 0,
            null => 0,
            var item => throw new InvalidOperationException($"Unsupported persistence work item kind: {item.Kind}")
        };
    }
}
