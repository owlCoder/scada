using Api.Authorization;
using Api.Contracts;
using Application.Detection;
using Application.Identity;
using Domain.Process.Commands;
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

        app.MapPut("/api/detections/rules/{ruleId}", async (string ruleId, RuleSettingsRequest request, DetectionRuleAdministrationService rules,
            HttpContext http, CancellationToken cancellationToken) =>
        {
            var settings = new DetectionRuleSettings(ruleId, request.Enabled, request.Severity, request.Threshold,
                request.WindowSeconds, request.CooldownSeconds);
            var result = await rules.UpdateAsync(settings, http.Actor(), cancellationToken);
            return result.Accepted ? Results.Ok(settings) : result.Reason switch
            {
                CommandRejectionReason.UnknownDevice => Results.NotFound(),
                CommandRejectionReason.NotAuthorized => Results.Forbid(),
                CommandRejectionReason.InvalidValue => Results.BadRequest("Invalid rule settings."),
                _ => result.ToHttp()
            };
        }).RequireRole(AccessRole.Administrator).RequireCredentialRateLimit();
        return app;
    }
}
