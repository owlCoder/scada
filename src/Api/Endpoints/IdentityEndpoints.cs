using Api.Authorization;
using Security;

namespace Api.Endpoints;

public static class IdentityEndpoints
{
    public static void Map(WebApplication app)
    {
        var read = app.MapGroup("/api").RequireRole(AccessRole.Viewer);
        read.MapGet("/me", (AuthorizationService authorization, HttpContext http) =>
        {
            var user = authorization.Authenticate(http.Request.Headers["X-Api-Key"].ToString())!;
            return Results.Ok(new { user.Username, role = user.Role.ToString() });
        });
    }
}
