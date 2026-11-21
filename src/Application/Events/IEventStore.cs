using Application.Common;

namespace Application.Events;

public interface IEventStore
{
    Task<int> AppendAsync(IReadOnlyList<SecurityEvent> events, CancellationToken cancellationToken);
    Task<PagedResult<SecurityEvent>> QueryAsync(EventQuery query, CancellationToken cancellationToken);
}
