using Application.Events;
using Application.Identity;
using Application.Persistence;
using Application.Realtime;
using Application.Simulation;
using Domain.Process.Alarms;

namespace Application.Alarms;

public sealed class AlarmService(
    SimulationEngine engine,
    IEventPublisher events,
    IPersistenceQueue persistence,
    IRealtimePublisher realtime,
    IAlarmHistoryStore history,
    TimeProvider time)
{
    public IReadOnlyList<ProcessAlarm> GetCurrent() => engine.Read(world => world.Plant.CaptureSnapshot().Alarms);

    public Task<IReadOnlyList<AlarmHistoryRecord>> GetHistoryAsync(int count, CancellationToken cancellationToken) =>
        history.GetRecentAsync(Math.Clamp(count, 1, 1000), cancellationToken);

    public bool Acknowledge(string key, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.HasRole(AccessRole.Operator) || string.IsNullOrWhiteSpace(key))
            return false;

        var now = time.GetUtcNow();
        var transition = engine.Mutate(world => world.Plant.AcknowledgeAlarm(key, actor.Name, now));
        if (transition is null)
            return false;

        var alarm = transition.Alarm;
        persistence.TryEnqueue(new AlarmHistoryWorkItem(new AlarmHistoryRecord(Guid.NewGuid(), now, alarm.Key, alarm.SourceId,
            alarm.Type, alarm.Severity, AlarmTransitionKind.Acknowledged, alarm.Message, actor.Name)));
        events.Publish(SecurityEvent.Create(now, EventCategories.Audit, EventSeverity.Information, "Hmi",
            $"Alarm {alarm.Key} acknowledged", actor.Name, alarm.SourceId));
        realtime.Publish(RealtimeTopics.Alarms, GetCurrent());
        return true;
    }
}
