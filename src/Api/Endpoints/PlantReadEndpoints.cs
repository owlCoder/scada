using Api.Authorization;
using Security;
using Application;

namespace Api.Endpoints;

public static class PlantReadEndpoints
{
    public static void Map(WebApplication app)
    {
        var read = app.MapGroup("/api").RequireRole(AccessRole.Viewer);
        read.MapGet("/plant", (IPlantService plant) => Results.Ok(plant.GetState()));
    }
}
