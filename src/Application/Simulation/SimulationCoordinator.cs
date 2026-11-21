using Application.Diagnostics;
using Application.Alarms;
using Application.Events;
using Application.Persistence;
using Application.Realtime;
using Application.Telemetry;
using Domain.Process.Snapshots;
using Microsoft.Extensions.Logging;

namespace Application.Simulation;

/// <summary>
/// Drives the engine and fans out each step to events, the historian, persistence, observers and
/// real-time subscribers. Observers run after the engine lock is released.
/// </summary>
public sealed class SimulationCoordinator(
    SimulationEngine engine,
    IEventPublisher events,
    TelemetryHistorian historian,
    IPersistenceQueue persistence,
    IRealtimePublisher realtime,
    IEnumerable<ISimulationObserver> observers,
    TimeProvider time,
    ILogger<SimulationCoordinator> logger)
{
    private readonly ISimulationObserver[] _observers = observers.ToArray();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SimulationStepReport?> StepAsync(bool force, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var report = engine.Step(force);
            if (report is null)
                return null;

            await DistributeAsync(report, cancellationToken);
            return report;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> StepManyAsync(int count, CancellationToken cancellationToken)
    {
        if (count is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(count), "Step count must be between 1 and 1000.");

        var executed = 0;
        for (var i = 0; i < count; i++)
        {
            if (await StepAsync(true, cancellationToken) is not null)
                executed++;
        }

        return executed;
    }

    public PlantSnapshot Reset(string actor)
    {
        var snapshot = engine.Reset();
        events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.Audit, EventSeverity.Medium,
            "simulation", "Simulation reset to initial configuration", actor));
        persistence.TryEnqueue(new SimulationStateWorkItem(engine.CaptureState()));
        realtime.Publish(RealtimeTopics.Plant, snapshot);
        realtime.Publish(RealtimeTopics.Simulation, engine.Status);
        return snapshot;
    }

    public SimulationStatus SetPaused(bool paused, string actor)
    {
        var changed = paused ? engine.Pause() : engine.Resume();
        if (changed)
        {
            events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.Audit, EventSeverity.Information,
                "simulation", paused ? "Simulation paused" : "Simulation resumed", actor));
            persistence.TryEnqueue(new SimulationStateWorkItem(engine.CaptureState()));
        }

        var status = engine.Status;
        realtime.Publish(RealtimeTopics.Simulation, status);
        return status;
    }

    private async Task<int> DistributeAsync(SimulationStepReport report, CancellationToken cancellationToken)
    {
        foreach (var item in StepEventTranslator.Translate(report))
            events.Publish(item);

        foreach (var transition in report.Plant.AlarmTransitions)
        {
            var alarm = transition.Alarm;
            persistence.TryEnqueue(new AlarmHistoryWorkItem(new AlarmHistoryRecord(Guid.NewGuid(), report.Timestamp,
                alarm.Key, alarm.SourceId, alarm.Type, alarm.Severity, transition.Kind, alarm.Message, alarm.AcknowledgedBy)));
        }

        ScadaInstrumentation.SimulationSteps.Add(1);
        var stepCount = report.Plant.Snapshot.StepCount;
        if (stepCount % engine.Options.TelemetryIntervalSteps == 0)
        {
            var samples = historian.Record(report.Plant.Snapshot, report.Timestamp);
            persistence.TryEnqueue(new TelemetryBatchWorkItem(samples));
            realtime.Publish(RealtimeTopics.Telemetry, samples);
        }

        if (stepCount % engine.Options.StatePersistenceIntervalSteps == 0)
            persistence.TryEnqueue(new SimulationStateWorkItem(engine.CaptureState()));

        var notified = 0;
        foreach (var observer in _observers)
        {
            try
            {
                notified += await observer.OnStepAsync(report, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Simulation observer {Observer} failed", observer.GetType().Name);
            }
        }

        realtime.Publish(RealtimeTopics.Plant, report.Plant.Snapshot);
        realtime.Publish(RealtimeTopics.Controllers, report.Controllers);
        realtime.Publish(RealtimeTopics.Network, report.Network);
        if (report.Plant.AlarmTransitions.Count > 0)
            realtime.Publish(RealtimeTopics.Alarms, report.Plant.Snapshot.Alarms);
        realtime.Publish(RealtimeTopics.Simulation, engine.Status);
        return notified;
    }
}
