using Application;
using Domain;
using Infrastructure;
using Protocols;
using Security;
using Simulation;

namespace Api.Endpoints;

public static class IncidentAndScenarioEndpoints
{
    public static void Map(WebApplication app)
    {
        var incidentCommands = app.MapGroup("/api/incidents");
        incidentCommands.AddEndpointFilter(async (context, next) =>
        {
            var policy = context.HttpContext.RequestServices.GetRequiredService<AuthorizationService>();
            var key = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
            return policy.Authorize(key, AccessRole.Administrator)
                ? await next(context)
                : Results.Unauthorized();
        });

        app.MapPost("/api/scenarios/{id}", (string id, ScenarioRequest request, ScenarioService scenarios,
            AuthorizationService authorization, HttpContext http) =>
        {
            if (string.IsNullOrWhiteSpace(request.Actor))
                return Results.BadRequest("Оператер је обавезан.");

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

        incidentCommands.MapPost("/{id:guid}/status", (Guid id, IncidentStatusRequest request,
            IncidentService service, IEventStore events, AuthorizationService authorization, HttpContext http) =>
        {
            if (string.IsNullOrWhiteSpace(request.Actor) ||
                !Enum.IsDefined(request.Status))
                return Results.BadRequest("Неисправан статус или оператер.");

            try
            {
                var actor = authorization.Authenticate(http.Request.Headers["X-Api-Key"].ToString())!.Username;
                if (!service.TryUpdate(id, request.Status, actor, out var incident))
                    return Results.NotFound();

                events.Append(new(DateTimeOffset.UtcNow, "Audit",
                    $"Статус инцидента {id} промењен у {request.Status}", actor));
                return Results.Ok(incident);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(exception.Message);
            }
        });


    }
}
