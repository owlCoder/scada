namespace Domain.Process.Alarms;

/// <summary>
/// Alarm lifecycle following the usual SCADA convention: an alarm stays visible until it has both
/// cleared and been acknowledged.
/// </summary>
public sealed class AlarmBook
{
    private readonly Dictionary<string, ProcessAlarm> _alarms = new(StringComparer.Ordinal);

    public IReadOnlyList<ProcessAlarm> Current =>
        _alarms.Values.OrderByDescending(a => a.Severity).ThenBy(a => a.RaisedAt).ToArray();

    public IReadOnlyList<AlarmTransition> Update(IReadOnlyCollection<AlarmCondition> conditions, DateTimeOffset now)
    {
        var transitions = new List<AlarmTransition>();
        var activeKeys = conditions.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var condition in conditions)
        {
            if (_alarms.TryGetValue(condition.Key, out var existing) && existing.IsActive)
                continue;

            var alarm = new ProcessAlarm(condition.Key, condition.SourceId, condition.Type, condition.Severity,
                condition.Message, true, now, null, false, null, null);
            _alarms[condition.Key] = alarm;
            transitions.Add(new(alarm, AlarmTransitionKind.Raised));
        }

        foreach (var alarm in _alarms.Values.Where(a => a.IsActive && !activeKeys.Contains(a.Key)).ToArray())
        {
            var cleared = alarm with { IsActive = false, ClearedAt = now };
            transitions.Add(new(cleared, AlarmTransitionKind.Cleared));
            if (cleared.IsAcknowledged)
                _alarms.Remove(alarm.Key);
            else
                _alarms[alarm.Key] = cleared;
        }

        return transitions;
    }

    public AlarmTransition? Acknowledge(string key, string actor, DateTimeOffset now)
    {
        if (!_alarms.TryGetValue(key, out var alarm) || alarm.IsAcknowledged)
            return null;

        var acknowledged = alarm with { IsAcknowledged = true, AcknowledgedBy = actor, AcknowledgedAt = now };
        if (acknowledged.IsActive)
            _alarms[key] = acknowledged;
        else
            _alarms.Remove(key);
        return new AlarmTransition(acknowledged, AlarmTransitionKind.Acknowledged);
    }

    public int Restore(IEnumerable<ProcessAlarm> alarms)
    {
        _alarms.Clear();
        foreach (var alarm in alarms)
            _alarms[alarm.Key] = alarm;
        return _alarms.Count;
    }
}
