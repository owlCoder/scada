using Api.Authorization;
using Application.Alarms;
using Application.Identity;

namespace Api.Endpoints.Alarms;

public static class AlarmEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        var read = app.MapGroup("/api/alarms").RequireRole(AccessRole.Viewer);
        read.MapGet("", (AlarmService alarms) => Results.Ok(alarms.GetCurrent()));
        read.MapGet("/history", async (int? count, AlarmService alarms, CancellationToken cancellationToken) =>
            Results.Ok(await alarms.GetHistoryAsync(count ?? 100, cancellationToken)));

        app.MapPost("/api/alarms/{key}/acknowledge", (string key, AlarmService alarms, HttpContext http) =>
                alarms.Acknowledge(key, http.Actor()) ? Results.Ok() : Results.NotFound())
            .RequireRole(AccessRole.Operator).RequireCredentialRateLimit();
        return app;
    }
}
