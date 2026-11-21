using Application.Common;
using Application.Incidents;
using Domain.Incidents;
using Infrastructure.Persistence.InMemory;

namespace FrontendTests.Support;

public sealed class BlockingIncidentStore : IIncidentStore
{
    private readonly InMemoryIncidentStore _store = new();
    public int Calls { get; private set; }
    public CancellationToken LastToken { get; private set; }
    public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<PagedResult<Incident>> QueryAsync(IncidentQuery query, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        if (++Calls > 1)
        {
            Blocked.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        return PagedResult<Incident>.From([], query.Page, query.PageSize);
    }

    public Task<Incident?> GetAsync(Guid id, CancellationToken cancellationToken) => _store.GetAsync(id, cancellationToken);
    public Task<Incident?> FindActiveByCorrelationAsync(string correlationKey, CancellationToken cancellationToken) => _store.FindActiveByCorrelationAsync(correlationKey, cancellationToken);
    public Task<Incident> AddAsync(Incident incident, IReadOnlyList<IncidentTimelineEntry> timeline, CancellationToken cancellationToken) => _store.AddAsync(incident, timeline, cancellationToken);
    public Task<bool> UpdateAsync(Incident incident, int expectedVersion, IReadOnlyList<IncidentTimelineEntry> timeline,
        CancellationToken cancellationToken, IncidentComment? comment = null) => _store.UpdateAsync(incident, expectedVersion, timeline, cancellationToken, comment);
    public Task<bool> AddCommentAsync(IncidentComment comment, IncidentTimelineEntry entry, CancellationToken cancellationToken) => _store.AddCommentAsync(comment, entry, cancellationToken);
    public Task<IReadOnlyList<IncidentComment>> GetCommentsAsync(Guid incidentId, CancellationToken cancellationToken) => _store.GetCommentsAsync(incidentId, cancellationToken);
    public Task<IReadOnlyList<IncidentTimelineEntry>> GetTimelineAsync(Guid incidentId, CancellationToken cancellationToken) => _store.GetTimelineAsync(incidentId, cancellationToken);
}
