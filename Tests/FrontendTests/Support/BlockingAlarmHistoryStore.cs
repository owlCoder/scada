using Application.Alarms;

namespace FrontendTests.Support;

public sealed class BlockingAlarmHistoryStore : IAlarmHistoryStore
{
    public int Calls { get; private set; }
    public CancellationToken LastToken { get; private set; }
    public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<int> AppendAsync(IReadOnlyList<AlarmHistoryRecord> records, CancellationToken cancellationToken) => Task.FromResult(records.Count);

    public async Task<IReadOnlyList<AlarmHistoryRecord>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        if (++Calls > 1)
        {
            Blocked.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        return [];
    }
}
