using Api.Authorization;
using Security;

namespace Api.Endpoints;

public static class ScenarioEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/scenarios/{id}", (string id, ScenarioService scenarios,
            AuthorizationService authorization, HttpContext http) =>
        {
            return scenarios.Execute(id, authorization.Authenticate(http.Request.Headers["X-Api-Key"].ToString())!.Username)
                ? Results.Accepted($"/api/scenarios/{id}")
                : Results.NotFound();
        }).RequireRole(AccessRole.Administrator);
    }
}
