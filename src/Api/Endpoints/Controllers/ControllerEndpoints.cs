using Api.Authorization;
using Application.Identity;
using Application.Simulation;
using Application.Training;

namespace Api.Endpoints.Controllers;

public static class ControllerEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        app.MapGet("/api/controllers", (SimulationEngine engine) =>
            Results.Ok(engine.Read(world => world.Controllers.Select(c => c.Snapshot()).ToArray()))).RequireRole(AccessRole.Viewer);

        app.MapPost("/api/controllers/{id}/{operation}", (string id, string operation, ITrainingEnvironment training) =>
        {
            bool? done = operation.ToLowerInvariant() switch
            {
                "start" => training.SetControllerRunning(id, true),
                "stop" => training.SetControllerRunning(id, false),
                "reset" => training.ResetController(id),
                "release-forces" => training.ReleaseRegisterForces(id) >= 0,
                _ => null
            };
            return done switch
            {
                null => Results.BadRequest("Operation must be start, stop, reset or release-forces."),
                true => Results.Ok(new { controller = id, operation }),
                false => Results.NotFound()
            };
        }).RequireRole(AccessRole.Administrator).RequireCredentialRateLimit();
        return app;
    }
}
