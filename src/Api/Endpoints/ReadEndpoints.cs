using Application;
using Protocols;
using Security;

namespace Api.Endpoints;

public static class ReadEndpoints
{
    public static void Map(WebApplication app)
    {
        var read = app.MapGroup("/api");
        read.AddEndpointFilter(async (context, next) =>
        {
            var policy = context.HttpContext.RequestServices.GetRequiredService<AuthorizationService>();
            var key = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
            return policy.Authorize(key, AccessRole.Viewer)
                ? await next(context)
                : Results.Unauthorized();
        });

        read.MapGet("/me", (AuthorizationService authorization, HttpContext http) =>
        {
            var user = authorization.Authenticate(http.Request.Headers["X-Api-Key"].ToString())!;
            return Results.Ok(new { user.Username, role = user.Role.ToString() });
        });
        read.MapGet("/plant", (IPlantService plant) => Results.Ok(plant.GetState()));
        read.MapGet("/events", (IEventStore store) => Results.Ok(store.GetRecent()));
        read.MapGet("/telemetry", (int? count, ITelemetryStore store) =>
            count is < 1 or > 3600
                ? Results.BadRequest("Sample count must be between 1 and 3600.")
                : Results.Ok(store.GetRecent(count ?? 100)));
        read.MapGet("/incidents", (IncidentService service) => Results.Ok(service.GetAll()));
        read.MapGet("/scenarios", (ScenarioService scenarios) => Results.Ok(scenarios.GetAvailable()));
        read.MapGet("/registers", (VirtualRegisterService registers) => Results.Ok(registers.ReadAll()));
        read.MapGet("/registers/{address:int}", (int address, VirtualRegisterService registers) =>
            address is >= 0 and <= ushort.MaxValue && registers.Read((ushort)address) is { } register
                ? Results.Ok(register)
                : Results.NotFound());
    }
}
