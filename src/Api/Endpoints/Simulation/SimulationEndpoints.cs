using Api.Authorization;
using Api.Contracts;
using Application.Identity;
using Application.Simulation;

namespace Api.Endpoints.Simulation;

public static class SimulationEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        app.MapGet("/api/simulation", (SimulationEngine engine) => Results.Ok(engine.Status)).RequireRole(AccessRole.Viewer);

        var control = app.MapGroup("/api/simulation").RequireRole(AccessRole.Operator).RequireCredentialRateLimit();
        control.MapPost("/pause", (SimulationCoordinator coordinator, HttpContext http) =>
            Results.Ok(coordinator.SetPaused(true, http.Actor())));
        control.MapPost("/resume", (SimulationCoordinator coordinator, HttpContext http) =>
            Results.Ok(coordinator.SetPaused(false, http.Actor())));
        control.MapPost("/step", async (StepRequest? request, SimulationCoordinator coordinator, SimulationEngine engine,
            CancellationToken cancellationToken) =>
        {
            var count = request?.Count ?? 1;
            if (count is < 1 or > 1000)
                return Results.BadRequest("Step count must be between 1 and 1000.");
            var executed = await coordinator.StepManyAsync(count, cancellationToken);
            return Results.Ok(new { executed, status = engine.Status });
        });
        app.MapPost("/api/simulation/reset", (SimulationCoordinator coordinator, HttpContext http) =>
                Results.Ok(coordinator.Reset(http.Actor().Name)))
            .RequireRole(AccessRole.Administrator).RequireCredentialRateLimit();
        return app;
    }
}
