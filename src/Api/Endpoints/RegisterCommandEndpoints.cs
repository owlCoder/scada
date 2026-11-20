using Api.Contracts;
using Application;
using Domain;
using Infrastructure;
using Protocols;
using Security;
using Simulation;

namespace Api.Endpoints;

public static class RegisterCommandEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/registers/{address:int}", (int address, RegisterWrite request,
            VirtualRegisterService registers, CommandMonitor monitor, HttpContext http) =>
        {
            if (address is < 0 or > ushort.MaxValue)
                return Results.NotFound();
            if (string.IsNullOrWhiteSpace(request.Actor))
                return Results.BadRequest("Оператер је обавезан.");
            if (!monitor.Check(ApiKeyValidator.Fingerprint(http.Request.Headers["X-Api-Key"].ToString())))
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            try
            {
                return registers.Write((ushort)address, request.Value,
                    http.RequestServices.GetRequiredService<AuthorizationService>()
                        .Authenticate(http.Request.Headers["X-Api-Key"].ToString())!.Username)
                    ? Results.Ok(registers.Read((ushort)address))
                    : Results.BadRequest("Регистар није уписив или вредност није дозвољена.");
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(exception.Message);
            }
        }).AddEndpointFilter(async (context, next) =>
        {
            var policy = context.HttpContext.RequestServices.GetRequiredService<AuthorizationService>();
            var key = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
            return policy.Authorize(key, AccessRole.Operator)
                ? await next(context)
                : Results.Unauthorized();
        });


    }
}
