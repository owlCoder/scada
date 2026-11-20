using Api.Authorization;
using Security;
using Application;

namespace Api.Endpoints;

public static class IncidentReadEndpoints
{
    public static void Map(WebApplication app)
    {
        var read = app.MapGroup("/api").RequireRole(AccessRole.Viewer);
        read.MapGet("/incidents", (IncidentService service) => Results.Ok(service.GetAll()));
    }
}
