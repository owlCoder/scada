using Api.Authorization;
using Api.Contracts;
using Api.Endpoints;
using Application.Detection;
using Application.Identity;
using Domain.Process.Commands;
using Security.Grid.Detection;
using Security.Grid.Scenarios;

namespace Api.Endpoints.GridSecurity;

/// <summary>
/// Grid-mode security endpoints: <c>/api/grid/scenarios...</c> (training scenarios, Administrator to control) and
/// <c>/api/grid/detections/rules|metrics</c> (detection rules, Administrator to tune). Same conventions as the water endpoints.
/// </summary>
public static class GridSecurityEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        MapScenarios(app);
        MapDetections(app);
        return app;
    }

    private static WebApplication MapScenarios(WebApplication app)
    {
        var read = app.MapGroup("/api/grid/scenarios").RequireRole(AccessRole.Viewer);
        read.MapGet("", (GridScenarioEngine scenarios) => Results.Ok(scenarios.Scenarios));
        read.MapGet("/current", (GridScenarioEngine scenarios) =>
            scenarios.Current is { } current ? Results.Ok(current) : Results.NoContent());
        read.MapGet("/runs", async (int? limit, GridScenarioEngine scenarios, CancellationToken cancellationToken) =>
            Results.Ok(await scenarios.ListRunsAsync(limit ?? 50, cancellationToken)));
        read.MapGet("/runs/{runId:guid}", async (Guid runId, GridScenarioEngine scenarios, CancellationToken cancellationToken) =>
            await scenarios.GetRunAsync(runId, cancellationToken) is { } run ? Results.Ok(run) : Results.NotFound());

        // Unknown scenarios are rejected before the rate limiter so typos do not consume the caller's budget.
        var control = app.MapGroup("/api/grid/scenarios").RequireRole(AccessRole.Administrator);
        control.MapPost("/{id}", StartAsync).AddEndpointFilter(KnownScenario).RequireCredentialRateLimit();
        control.MapPost("/{id}/start", StartAsync).AddEndpointFilter(KnownScenario).RequireCredentialRateLimit();
        var lifecycle = control.MapGroup("").RequireCredentialRateLimit();
        lifecycle.MapPost("/active/pause", async (GridScenarioEngine scenarios, HttpContext http, CancellationToken cancellationToken) =>
            (await scenarios.PauseAsync(http.Actor(), cancellationToken)).ToHttp());
        lifecycle.MapPost("/active/resume", async (GridScenarioEngine scenarios, HttpContext http, CancellationToken cancellationToken) =>
            (await scenarios.ResumeAsync(http.Actor(), cancellationToken)).ToHttp());
        lifecycle.MapPost("/active/stop", async (GridScenarioEngine scenarios, HttpContext http, CancellationToken cancellationToken) =>
            (await scenarios.StopAsync(http.Actor(), cancellationToken)).ToHttp());
        lifecycle.MapPost("/reset", async (GridScenarioEngine scenarios, HttpContext http, CancellationToken cancellationToken) =>
            (await scenarios.ResetAsync(http.Actor(), cancellationToken)).ToHttp());
        lifecycle.MapPost("/runs/{runId:guid}/replay", async (Guid runId, GridScenarioEngine scenarios, HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var result = await scenarios.ReplayAsync(runId, http.Actor(), cancellationToken);
            return result.Succeeded ? Results.Accepted($"/api/grid/scenarios/runs/{result.Run!.RunId}", result) : result.ToHttp();
        });
        return app;
    }

    private static WebApplication MapDetections(WebApplication app)
    {
        var read = app.MapGroup("/api/grid/detections").RequireRole(AccessRole.Viewer);
        read.MapGet("", async (string? ruleId, Guid? scenarioRunId, DateTimeOffset? since, int? limit, IDetectionStore store,
            CancellationToken cancellationToken) =>
        {
            var query = new DetectionQuery(ruleId, scenarioRunId, since, Math.Clamp(limit ?? 200, 1, 1000));
            var detections = await store.QueryAsync(query, cancellationToken);
            // Without an explicit rule filter only grid detections are returned; the store is shared with the water mode.
            var grid = ruleId is null
                ? detections.Where(d => d.RuleId.StartsWith("grid-", StringComparison.Ordinal)).ToArray()
                : detections;
            return Results.Ok(grid);
        });
        read.MapGet("/rules", (GridDetectionEngine detection) => Results.Ok(detection.Rules));
        read.MapGet("/metrics", (GridDetectionEngine detection) => Results.Ok(detection.Metrics));

        app.MapPut("/api/grid/detections/rules/{ruleId}", async (string ruleId, RuleSettingsRequest request,
            GridDetectionRuleAdministrationService rules, HttpContext http, CancellationToken cancellationToken) =>
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

    private static async Task<IResult> StartAsync(string id, GridScenarioEngine scenarios, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await scenarios.StartAsync(id, http.Actor(), cancellationToken);
        return result.Succeeded ? Results.Accepted($"/api/grid/scenarios/runs/{result.Run!.RunId}", result) : Results.Conflict(result);
    }

    private static async ValueTask<object?> KnownScenario(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var id = context.HttpContext.GetRouteValue("id") as string;
        var scenarios = context.HttpContext.RequestServices.GetRequiredService<GridScenarioEngine>();
        return scenarios.Scenarios.Any(s => s.Id == id) ? await next(context) : Results.NotFound();
    }
}
