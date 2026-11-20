using Api.Contracts;
using Application;
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
        }).AddEndpointFilter(async (context, next) =>
        {
            var policy = context.HttpContext.RequestServices.GetRequiredService<AuthorizationService>();
            var key = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
            return policy.Authorize(key, AccessRole.Administrator)
                ? await next(context)
                : Results.Unauthorized();
        });
    }
}
