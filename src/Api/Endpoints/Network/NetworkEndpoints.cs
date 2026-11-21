using Api.Authorization;
using Api.Contracts;
using Application.Identity;
using Application.Simulation;
using Application.Training;

namespace Api.Endpoints.Network;

public static class NetworkEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        app.MapGet("/api/network", (SimulationEngine engine) => Results.Ok(engine.Read(world => world.Network.Snapshot())))
            .RequireRole(AccessRole.Viewer);

        var control = app.MapGroup("/api/network/links/{id}").RequireRole(AccessRole.Administrator).RequireCredentialRateLimit();
        control.MapPost("/connect", (string id, ITrainingEnvironment training) =>
            training.SetLinkConnected(id, true) ? Results.Ok() : Results.Conflict("Link unknown or already connected."));
        control.MapPost("/disconnect", (string id, ITrainingEnvironment training) =>
            training.SetLinkConnected(id, false) ? Results.Ok() : Results.Conflict("Link unknown or already disconnected."));
        control.MapPost("/impairment", (string id, LinkImpairmentRequest request, ITrainingEnvironment training) =>
            training.SetLinkImpairment(id, request.LatencyMilliseconds, request.PacketLossPercent)
                ? Results.Ok()
                : Results.BadRequest("Link unknown or impairment out of range."));
        return app;
    }
}
