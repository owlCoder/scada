using Api.Contracts;
using Application;
using Security;

namespace Api.Endpoints;

public static class PlantCommandEndpoints
{
    public static void Map(WebApplication app)
    {
        var commands = app.MapGroup("/api/plant");
        commands.AddEndpointFilter(async (context, next) =>
        {
            var policy = context.HttpContext.RequestServices.GetRequiredService<AuthorizationService>();
            var key = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
            return policy.Authorize(key, AccessRole.Operator)
                ? await next(context)
                : Results.Unauthorized();
        });

        commands.MapPost("/tick", (TickRequest request, IPlantService service, CommandMonitor monitor, HttpContext http) =>
        {
            if (!double.IsFinite(request.Seconds) || request.Seconds is <= 0 or > 60)
                return Results.BadRequest("Step must be between 0 and 60 seconds.");
            if (!monitor.Check(ApiKeyValidator.Fingerprint(http.Request.Headers["X-Api-Key"].ToString())))
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            service.Advance(request.Seconds);
            return Results.Ok(service.GetState());
        });

        commands.MapPost("/pump/{id}/command", (string id, PumpCommand command, IPlantService service, CommandMonitor monitor, HttpContext http) =>
        {
            if (id != "P-101")
                return Results.NotFound();
            if (string.IsNullOrWhiteSpace(command.Actor))
                return Results.BadRequest("Operator is required.");
            if (!monitor.Check(ApiKeyValidator.Fingerprint(http.Request.Headers["X-Api-Key"].ToString())))
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            try
            {
                service.SetPump(command.Running, http.RequestServices.GetRequiredService<AuthorizationService>()
                    .Authenticate(http.Request.Headers["X-Api-Key"].ToString())!.Username);
                return Results.Ok(service.GetState());
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(exception.Message);
            }
        });

        commands.MapPost("/valve/{id}/command", (string id, ValveCommand command, IPlantService service, CommandMonitor monitor, HttpContext http) =>
        {
            if (id != "V-101")
                return Results.NotFound();
            if (string.IsNullOrWhiteSpace(command.Actor))
                return Results.BadRequest("Operator is required.");
            if (!monitor.Check(ApiKeyValidator.Fingerprint(http.Request.Headers["X-Api-Key"].ToString())))
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            service.SetOutletValve(command.Open, http.RequestServices.GetRequiredService<AuthorizationService>()
                .Authenticate(http.Request.Headers["X-Api-Key"].ToString())!.Username);
            return Results.Ok(service.GetState());
        });
    }
}
