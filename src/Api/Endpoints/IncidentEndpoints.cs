using Api.Authorization;
using Api.Contracts;
using Application;
using Security;

namespace Api.Endpoints;

public static class IncidentEndpoints
{
    public static void Map(WebApplication app)
    {
        var incidentCommands = app.MapGroup("/api/incidents");
        incidentCommands.RequireRole(AccessRole.Administrator);

        incidentCommands.MapPost("/{id:guid}/status", (Guid id, IncidentStatusRequest request,
            IncidentService service, IEventStore events, AuthorizationService authorization,
            CommandMonitor monitor, HttpContext http) =>
        {
            if (!Enum.IsDefined(request.Status))
                return Results.BadRequest("Invalid incident status.");

            var apiKey = http.Request.Headers["X-Api-Key"].ToString();
            if (!monitor.Check(ApiKeyValidator.Fingerprint(apiKey)))
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            try
            {
                var actor = authorization.Authenticate(apiKey)!.Username;
                if (!service.TryUpdate(id, request.Status, actor, out var incident))
                    return Results.NotFound();

                events.Append(new(DateTimeOffset.UtcNow, "Audit",
                    $"Incident status {id} changed to {request.Status}", actor));
                return Results.Ok(incident);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(exception.Message);
            }
        });
    }
}
