using Api.Authorization;
using Application.Identity;

namespace Api.Endpoints.Identity;

public static class IdentityEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        app.MapGet("/api/me", (HttpContext http) =>
        {
            var user = http.Principal().User;
            return Results.Ok(new { user.Username, role = user.Role.ToString() });
        }).RequireRole(AccessRole.Viewer);
        return app;
    }
}
