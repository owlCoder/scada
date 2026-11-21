namespace Application.Alarms;

public interface IAlarmHistoryStore
{
    Task<int> AppendAsync(IReadOnlyList<AlarmHistoryRecord> records, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlarmHistoryRecord>> GetRecentAsync(int count, CancellationToken cancellationToken);
}
