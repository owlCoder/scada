using Application.Common;
using Application.Events;

namespace FrontendTests.Support;

public sealed class FailingEventStore : IEventStore
{
    public bool Fail { get; set; } = true;
    public int Calls { get; private set; }

    public Task<int> AppendAsync(IReadOnlyList<SecurityEvent> events, CancellationToken cancellationToken) => Task.FromResult(events.Count);

    public Task<PagedResult<SecurityEvent>> QueryAsync(EventQuery query, CancellationToken cancellationToken)
    {
        Calls++;
        if (Fail)
            throw new InvalidOperationException("private database failure");
        return Task.FromResult(PagedResult<SecurityEvent>.From([], 1, 50));
    }
}
