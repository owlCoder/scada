using Application.Persistence;
using Application.Simulation;

namespace Api.Endpoints.System;

public static class HealthEndpoints
{
    private const double SaturationThreshold = 0.9;

    public static WebApplication Map(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", storage = "memory" }));
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapGet("/health/ready", (IPersistenceQueue queue, SimulationEngine engine) =>
        {
            var persistence = queue.Status;
            var saturated = persistence.Pending >= persistence.Capacity * SaturationThreshold;
            // Anonymous endpoint: expose counters only, never error text.
            var body = new
            {
                status = saturated ? "degraded" : "ready",
                persistence = new { persistence.Pending, persistence.Capacity, persistence.Dropped, persistence.Failed },
                simulation = new { engine.Status.IsPaused, engine.Status.StepCount }
            };
            return saturated ? Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable) : Results.Ok(body);
        });
        return app;
    }
}
