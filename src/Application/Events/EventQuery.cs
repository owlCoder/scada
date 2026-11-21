using Application.Common;

namespace Application.Events;

/// <summary><see cref="ExcludeTraining"/> hides <see cref="EventCategories.Scenario"/> events (injected faults, spoofing,
/// protection tampering) from readers who are not instructors, so the exercise is not given away.</summary>
public sealed record EventQuery(
    string? Category = null,
    string? Actor = null,
    string? DeviceId = null,
    string? Search = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50,
    bool ExcludeTraining = false)
{
    public EventQuery Normalize() => this with
    {
        Category = QueryText.Normalize(Category),
        Actor = QueryText.Normalize(Actor),
        DeviceId = QueryText.Normalize(DeviceId),
        Search = QueryText.Normalize(Search)
    };
}
