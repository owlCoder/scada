using Api.Authorization;
using Security;
using Application;

namespace Api.Endpoints;

public static class EventReadEndpoints
{
    public static void Map(WebApplication app)
    {
        var read = app.MapGroup("/api").RequireRole(AccessRole.Viewer);
        read.MapGet("/events", (IEventStore store) => Results.Ok(store.GetRecent()));
        read.MapGet("/telemetry", (int? count, ITelemetryStore store) =>
            count is < 1 or > 3600
                ? Results.BadRequest("Sample count must be between 1 and 3600.")
                : Results.Ok(store.GetRecent(count ?? 100)));
    }
}
