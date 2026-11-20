using Api.Authorization;
using Security;

namespace Api.Endpoints;

public static class ScenarioEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/scenarios/{id}", (string id, ScenarioService scenarios,
            AuthorizationService authorization, CommandMonitor monitor, HttpContext http) =>
        {
            var apiKey = http.Request.Headers["X-Api-Key"].ToString();
            if (!scenarios.GetAvailable().Any(scenario => scenario.Id == id))
                return Results.NotFound();

            if (!monitor.Check(ApiKeyValidator.Fingerprint(apiKey)))
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            return scenarios.Execute(id, authorization.Authenticate(apiKey)!.Username)
                ? Results.Accepted($"/api/scenarios/{id}")
                : Results.NotFound();
        }).RequireRole(AccessRole.Administrator);
    }
}
