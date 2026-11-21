using Api.Authorization;
using Application.Identity;
using Application.Telemetry;

namespace Api.Endpoints.Telemetry;

public static class TelemetryEndpoints
{
    private const int MaxCount = 3600;

    public static WebApplication Map(WebApplication app)
    {
        var read = app.MapGroup("/api").RequireRole(AccessRole.Viewer);
        read.MapGet("/telemetry", async (string? tagId, int? count, DateTimeOffset? from, DateTimeOffset? to,
            ITelemetryStore store, CancellationToken cancellationToken) =>
            count is < 1 or > MaxCount
                ? Results.BadRequest($"Sample count must be between 1 and {MaxCount}.")
                : Results.Ok(await store.QueryAsync(new TelemetryQuery(tagId, from, to, count ?? 100), cancellationToken)));
        read.MapGet("/telemetry/tags", (TelemetryHistorian historian) => Results.Ok(historian.Tags()));
        read.MapGet("/telemetry/integrity", (TelemetryHistorian historian) => Results.Ok(historian.Verify()));
        read.MapGet("/trends/{tagId}", (string tagId, int? count, TelemetryHistorian historian) =>
            count is < 1 or > MaxCount
                ? Results.BadRequest($"Sample count must be between 1 and {MaxCount}.")
                : Results.Ok(historian.GetSeries(tagId, count ?? 240)));
        return app;
    }
}
