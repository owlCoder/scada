using Api.Authorization;
using Api.Contracts;
using Security;

namespace Api.Endpoints;

public static class ScenarioEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/scenarios/{id}", (string id, ScenarioRequest request, ScenarioService scenarios,
            AuthorizationService authorization, HttpContext http) =>
        {
            if (string.IsNullOrWhiteSpace(request.Actor))
                return Results.BadRequest("Operator is required.");

            return scenarios.Execute(id, authorization.Authenticate(http.Request.Headers["X-Api-Key"].ToString())!.Username)
                ? Results.Accepted($"/api/scenarios/{id}")
                : Results.NotFound();
        }).RequireRole(AccessRole.Administrator);
    }
}
