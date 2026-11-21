using Api.Authorization;
using Api.Contracts;
using Application.Detection;
using Application.Identity;
using Security.Detection;

namespace Api.Endpoints.Detections;

public static class DetectionEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        var read = app.MapGroup("/api/detections").RequireRole(AccessRole.Viewer);
        read.MapGet("", async (string? ruleId, Guid? scenarioRunId, DateTimeOffset? since, int? limit, IDetectionStore store,
            CancellationToken cancellationToken) =>
            Results.Ok(await store.QueryAsync(new DetectionQuery(ruleId, scenarioRunId, since, Math.Clamp(limit ?? 200, 1, 1000)),
                cancellationToken)));
        read.MapGet("/rules", (DetectionEngine detection) => Results.Ok(detection.Rules));
        read.MapGet("/metrics", (DetectionEngine detection) => Results.Ok(detection.Metrics));

        app.MapPut("/api/detections/rules/{ruleId}", async (string ruleId, RuleSettingsRequest request, DetectionEngine detection,
            IDetectionRuleSettingsStore store, CancellationToken cancellationToken) =>
        {
            var settings = new DetectionRuleSettings(ruleId, request.Enabled, request.Severity, request.Threshold,
                request.WindowSeconds, request.CooldownSeconds);
            if (detection.Rules.All(r => r.Id != ruleId))
                return Results.NotFound();
            if (!detection.UpdateSettings(settings))
                return Results.BadRequest("Invalid rule settings.");
            await store.SaveAsync(settings, cancellationToken);
            return Results.Ok(settings);
        }).RequireRole(AccessRole.Administrator).RequireCredentialRateLimit();
        return app;
    }
}
