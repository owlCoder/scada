using Application.Common;
using Domain.Incidents;

namespace Application.Incidents;

public interface IIncidentStore
{
    Task<Incident?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Incident?> FindActiveByCorrelationAsync(string correlationKey, CancellationToken cancellationToken);
    Task<PagedResult<Incident>> QueryAsync(IncidentQuery query, CancellationToken cancellationToken);

    /// <summary>Inserts a new incident with its timeline and returns it with the assigned incident number.</summary>
    Task<Incident> AddAsync(Incident incident, IReadOnlyList<IncidentTimelineEntry> timeline, CancellationToken cancellationToken);

    /// <summary>Optimistic update: succeeds only if the stored version equals <paramref name="expectedVersion"/>.</summary>
    Task<bool> UpdateAsync(Incident incident, int expectedVersion, IReadOnlyList<IncidentTimelineEntry> timeline,
        CancellationToken cancellationToken, IncidentComment? comment = null);

    Task<bool> AddCommentAsync(IncidentComment comment, IncidentTimelineEntry entry, CancellationToken cancellationToken);
    Task<IReadOnlyList<IncidentComment>> GetCommentsAsync(Guid incidentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<IncidentTimelineEntry>> GetTimelineAsync(Guid incidentId, CancellationToken cancellationToken);
}
