using Api.Authorization;
using Application.Identity;
using Security.Scenarios;

namespace Api.Endpoints.Scenarios;

public static class ScenarioEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        var read = app.MapGroup("/api/scenarios").RequireRole(AccessRole.Viewer);
        read.MapGet("", (ScenarioEngine scenarios) => Results.Ok(scenarios.Scenarios));
        read.MapGet("/current", (ScenarioEngine scenarios) =>
            scenarios.Current is { } current ? Results.Ok(current) : Results.NoContent());
        read.MapGet("/runs", async (int? limit, ScenarioEngine scenarios, CancellationToken cancellationToken) =>
            Results.Ok(await scenarios.ListRunsAsync(limit ?? 50, cancellationToken)));
        read.MapGet("/runs/{runId:guid}", async (Guid runId, ScenarioEngine scenarios, CancellationToken cancellationToken) =>
            await scenarios.GetRunAsync(runId, cancellationToken) is { } run ? Results.Ok(run) : Results.NotFound());

        // Unknown scenarios are rejected before the rate limiter so typos do not consume the caller's budget.
        var control = app.MapGroup("/api/scenarios").RequireRole(AccessRole.Administrator);
        control.MapPost("/{id}", StartAsync).AddEndpointFilter(KnownScenario).RequireCredentialRateLimit();
        control.MapPost("/{id}/start", StartAsync).AddEndpointFilter(KnownScenario).RequireCredentialRateLimit();
        var lifecycle = control.MapGroup("").RequireCredentialRateLimit();
        lifecycle.MapPost("/active/pause", async (ScenarioEngine scenarios, HttpContext http, CancellationToken cancellationToken) =>
            (await scenarios.PauseAsync(http.Actor(), cancellationToken)).ToHttp());
        lifecycle.MapPost("/active/resume", async (ScenarioEngine scenarios, HttpContext http, CancellationToken cancellationToken) =>
            (await scenarios.ResumeAsync(http.Actor(), cancellationToken)).ToHttp());
        lifecycle.MapPost("/active/stop", async (ScenarioEngine scenarios, HttpContext http, CancellationToken cancellationToken) =>
            (await scenarios.StopAsync(http.Actor(), cancellationToken)).ToHttp());
        lifecycle.MapPost("/reset", async (ScenarioEngine scenarios, HttpContext http, CancellationToken cancellationToken) =>
            (await scenarios.ResetAsync(http.Actor(), cancellationToken)).ToHttp());
        lifecycle.MapPost("/runs/{runId:guid}/replay", async (Guid runId, ScenarioEngine scenarios, HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var result = await scenarios.ReplayAsync(runId, http.Actor(), cancellationToken);
            return result.Succeeded ? Results.Accepted($"/api/scenarios/runs/{result.Run!.RunId}", result) : result.ToHttp();
        });
        return app;
    }

    private static async Task<IResult> StartAsync(string id, ScenarioEngine scenarios, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await scenarios.StartAsync(id, http.Actor(), cancellationToken);
        return result.Succeeded ? Results.Accepted($"/api/scenarios/runs/{result.Run!.RunId}", result) : Results.Conflict(result);
    }

    private static async ValueTask<object?> KnownScenario(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var id = context.HttpContext.GetRouteValue("id") as string;
        var scenarios = context.HttpContext.RequestServices.GetRequiredService<ScenarioEngine>();
        return scenarios.Scenarios.Any(s => s.Id == id) ? await next(context) : Results.NotFound();
    }
}
