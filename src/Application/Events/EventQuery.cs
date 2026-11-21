using Application.Common;

namespace Application.Events;

public sealed record EventQuery(
    string? Category = null,
    string? Actor = null,
    string? DeviceId = null,
    string? Search = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50)
{
    public EventQuery Normalize() => this with
    {
        Category = QueryText.Normalize(Category),
        Actor = QueryText.Normalize(Actor),
        DeviceId = QueryText.Normalize(DeviceId),
        Search = QueryText.Normalize(Search)
    };
}
