using Api.Authorization;
using Security;

namespace Api.Endpoints;

public static class ScenarioReadEndpoints
{
    public static void Map(WebApplication app)
    {
        var read = app.MapGroup("/api").RequireRole(AccessRole.Viewer);
        read.MapGet("/scenarios", (ScenarioService scenarios) => Results.Ok(scenarios.GetAvailable()));
    }
}
