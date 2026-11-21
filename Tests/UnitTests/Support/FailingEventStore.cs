using Application.Common;
using Application.Events;

namespace UnitTests.Support;

public sealed class FailingEventStore : IEventStore
{
    public int Attempts { get; private set; }

    public Task<int> AppendAsync(IReadOnlyList<SecurityEvent> events, CancellationToken cancellationToken)
    {
        Attempts++;
        throw new InvalidOperationException("database unavailable");
    }

    public Task<PagedResult<SecurityEvent>> QueryAsync(EventQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<SecurityEvent>([], 1, 50, 0));
}
