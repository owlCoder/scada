using Application.Persistence;
using Application.Simulation;

namespace Api.Endpoints.System;

public static class HealthEndpoints
{
    private const double SaturationThreshold = 0.9;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    public static WebApplication Map(WebApplication app)
    {
        app.MapGet("/health", (IStorageProbe storage) => Results.Ok(new { status = "healthy", storage = storage.Provider }))
            .WithName(HealthFormatPolicy.JsonEndpoint)
            .WithOrder(-1);
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapGet("/health/ready", async (IPersistenceQueue queue, SimulationEngine engine, IStorageProbe storage,
            CancellationToken cancellationToken) =>
        {
            var persistence = queue.Status;
            var storageAvailable = await IsAvailableAsync(storage, cancellationToken);
            var degraded = persistence.Pending >= persistence.Capacity * SaturationThreshold || !storageAvailable;
            // Anonymous endpoint: expose counters only, never error text.
            var body = new
            {
                status = degraded ? "degraded" : "ready",
                storage = new { provider = storage.Provider, available = storageAvailable },
                persistence = new { persistence.Pending, persistence.Capacity, persistence.Dropped, persistence.Failed },
                simulation = new { engine.Status.IsPaused, engine.Status.StepCount }
            };
            return degraded ? Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable) : Results.Ok(body);
        });
        return app;
    }

    private static async Task<bool> IsAvailableAsync(IStorageProbe storage, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            return await storage.IsAvailableAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
