using Api.Authorization;
using Security;

namespace Api.Endpoints.Read;

public static class ScenarioReadEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        var read = app.MapGroup("/api").RequireRole(AccessRole.Viewer);
        read.MapGet("/scenarios", (ScenarioService scenarios) => Results.Ok(scenarios.GetAvailable()));
        return app;
    }
}
