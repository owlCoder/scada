using Api.Authorization;
using Security;
using Application;

namespace Api.Endpoints.Read;

public static class IncidentReadEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        var read = app.MapGroup("/api").RequireRole(AccessRole.Viewer);
        read.MapGet("/incidents", (IncidentService service) => Results.Ok(service.GetAll()));
        return app;
    }
}
