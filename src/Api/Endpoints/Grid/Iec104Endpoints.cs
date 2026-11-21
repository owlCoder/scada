using Api.Authorization;
using Application.Identity;
using Microsoft.Extensions.DependencyInjection;
using Protocols.Iec104;

namespace Api.Endpoints.Grid;

/// <summary>Documentation endpoint for the IEC 60870-5-104 address map (Viewer). Returns 404 while the protocol is disabled.</summary>
public static class Iec104Endpoints
{
    public static WebApplication Map(WebApplication app)
    {
        app.MapGet("/api/grid/iec104/map", (HttpContext http) =>
        {
            var map = http.RequestServices.GetService<Iec104AddressMap>();
            return map is null
                ? Results.NotFound("IEC 60870-5-104 is disabled (Protocols:Iec104:Enabled=false).")
                : Results.Ok(map);
        }).RequireRole(AccessRole.Viewer);
        return app;
    }
}
