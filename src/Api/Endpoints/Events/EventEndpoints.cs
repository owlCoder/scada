using Api.Authorization;
using Application.Events;
using Application.Identity;

namespace Api.Endpoints.Events;

public static class EventEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        var read = app.MapGroup("/api/events").RequireRole(AccessRole.Viewer);
        read.MapGet("", async (string? category, string? actor, string? deviceId, string? search, DateTimeOffset? from,
                DateTimeOffset? to, int? page, int? pageSize, IEventStore store, HttpContext http, CancellationToken cancellationToken) =>
            Results.Ok(await store.QueryAsync(new EventQuery(category, actor, deviceId, search, from, to, page ?? 1, pageSize ?? 50,
                ExcludeTraining: !http.Actor().HasRole(AccessRole.Administrator)), cancellationToken)));
        read.MapGet("/recent", (int? count, RecentEventBuffer buffer, HttpContext http) =>
        {
            var recent = buffer.GetRecent(count ?? 100);
            return Results.Ok(http.Actor().HasRole(AccessRole.Administrator)
                ? recent
                : recent.Where(e => e.Category != EventCategories.Scenario).ToArray());
        });
        return app;
    }
}
