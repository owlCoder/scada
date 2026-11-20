using Api.Authorization;
using Api.Contracts;
using Protocols;
using Security;

namespace Api.Endpoints.Commands;

public static class RegisterCommandEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        app.MapPost("/api/registers/{address:int}", (int address, RegisterWrite request,
            VirtualRegisterService registers, CommandMonitor monitor, HttpContext http) =>
        {
            if (address is < 0 or > ushort.MaxValue)
                return Results.NotFound();
            if (!monitor.Check(ApiKeyValidator.Fingerprint(http.Request.Headers["X-Api-Key"].ToString())))
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            try
            {
                return registers.Write((ushort)address, request.Value,
                    http.RequestServices.GetRequiredService<AuthorizationService>()
                        .Authenticate(http.Request.Headers["X-Api-Key"].ToString())!.Username)
                    ? Results.Ok(registers.Read((ushort)address))
                    : Results.BadRequest("Register is read-only or value is invalid.");
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(exception.Message);
            }
        }).RequireRole(AccessRole.Operator);
        return app;
    }
}
