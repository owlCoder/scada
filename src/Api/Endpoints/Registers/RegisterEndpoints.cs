using Api.Authorization;
using Api.Contracts;
using Application.Identity;
using Application.Registers;
using Domain.Control;

namespace Api.Endpoints.Registers;

public static class RegisterEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        var read = app.MapGroup("/api/registers").RequireRole(AccessRole.Viewer);
        read.MapGet("", (RegisterAccessService registers) => Results.Ok(registers.ListControllers()));
        read.MapGet("/{unit:int}/{area}/{address:int}", (int unit, string area, int address, int? count,
            RegisterAccessService registers, HttpContext http) =>
        {
            if (!TryArea(area, out var registerArea))
                return Results.BadRequest("Area must be holding or input.");
            if (unit is < 1 or > byte.MaxValue || address is < 0 or > ushort.MaxValue)
                return Results.NotFound();

            var length = count ?? 1;
            if (length is < 1 or > 125)
                return Results.BadRequest("Register count must be 1 to 125.");
            var result = registers.Read((byte)unit, registerArea, (ushort)address, (ushort)length, $"rest:{http.Actor().Name}");
            return result.Status switch
            {
                RegisterOperationStatus.Success => Results.Ok(result),
                RegisterOperationStatus.IllegalValue => Results.BadRequest(result),
                _ => Results.NotFound(result)
            };
        });

        app.MapPost("/api/registers/{unit:int}/holding/{address:int}", (int unit, int address, RegisterWriteRequest request,
            RegisterAccessService registers, HttpContext http) =>
        {
            if (unit is < 1 or > byte.MaxValue || address is < 0 or > ushort.MaxValue)
                return Results.NotFound();

            var result = registers.WriteSetpoint((byte)unit, (ushort)address, request.Value, http.Actor(), $"rest:{http.Actor().Name}");
            return result.Status switch
            {
                RegisterOperationStatus.Success => Results.Ok(result),
                RegisterOperationStatus.IllegalValue => Results.BadRequest(result),
                RegisterOperationStatus.Denied => Results.Json(result, statusCode: StatusCodes.Status403Forbidden),
                _ => Results.NotFound(result)
            };
        }).RequireRole(AccessRole.Operator).RequireCredentialRateLimit();
        return app;
    }

    private static bool TryArea(string area, out RegisterArea result)
    {
        result = area.ToLowerInvariant() == "input" ? RegisterArea.Input : RegisterArea.Holding;
        return area.Equals("input", StringComparison.OrdinalIgnoreCase) || area.Equals("holding", StringComparison.OrdinalIgnoreCase);
    }
}
