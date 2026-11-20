using Api.Authorization;
using Security;
using Protocols;

namespace Api.Endpoints;

public static class RegisterReadEndpoints
{
    public static void Map(WebApplication app)
    {
        var read = app.MapGroup("/api").RequireRole(AccessRole.Viewer);
        read.MapGet("/registers", (VirtualRegisterService registers) => Results.Ok(registers.ReadAll()));
        read.MapGet("/registers/{address:int}", (int address, VirtualRegisterService registers) =>
            address is >= 0 and <= ushort.MaxValue && registers.Read((ushort)address) is { } register
                ? Results.Ok(register)
                : Results.NotFound());
    }
}
