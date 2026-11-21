using Api.Authorization;
using Api.Contracts;
using Application.Commands;
using Application.Identity;
using Application.Training;

namespace Api.Endpoints.Plant;

public static class PlantEndpoints
{
    public static WebApplication Map(WebApplication app)
    {
        var read = app.MapGroup("/api/plant").RequireRole(AccessRole.Viewer);
        read.MapGet("", (ITrainingEnvironment plant) => Results.Ok(plant.Snapshot()));
        read.MapGet("/devices/{id}", (string id, ITrainingEnvironment plant) =>
        {
            var snapshot = plant.Snapshot();
            object? device = (object?)snapshot.Tank(id) ?? (object?)snapshot.Pump(id) ?? (object?)snapshot.Valve(id) ?? snapshot.Sensor(id);
            return device is null ? Results.NotFound() : Results.Ok(device);
        });

        var commands = app.MapGroup("/api").RequireRole(AccessRole.Operator).RequireCredentialRateLimit();
        commands.MapPost("/commands", (DeviceCommandRequest request, ControlCommandService service, HttpContext http) =>
            service.Execute(new OperatorCommand(request.DeviceId, request.Type, request.Value, CommandSource.RestApi,
                request.SequenceNumber), http.Actor()).ToHttp());

        commands.MapPost("/plant/pump/{id}/command", (string id, PumpCommandRequest request, ControlCommandService service, HttpContext http) =>
            service.Execute(new OperatorCommand(id, request.Running ? OperatorCommandType.StartPump : OperatorCommandType.StopPump,
                null, CommandSource.RestApi, request.SequenceNumber), http.Actor()).ToHttp());

        commands.MapPost("/plant/valve/{id}/command", (string id, ValveCommandRequest request, ControlCommandService service, HttpContext http) =>
        {
            var position = request.Position ?? (request.Open is { } open ? (open ? 100 : 0) : (double?)null);
            return position is null
                ? Results.BadRequest("Either position or open is required.")
                : service.Execute(new OperatorCommand(id, OperatorCommandType.SetValvePosition, position, CommandSource.RestApi,
                    request.SequenceNumber), http.Actor()).ToHttp();
        });

        commands.MapPost("/plant/devices/{id}/mode/{mode}", (string id, string mode, ControlCommandService service, HttpContext http) =>
            mode.ToLowerInvariant() switch
            {
                "manual" => service.Execute(new OperatorCommand(id, OperatorCommandType.SetManual, null, CommandSource.RestApi), http.Actor()).ToHttp(),
                "automatic" => service.Execute(new OperatorCommand(id, OperatorCommandType.SetAutomatic, null, CommandSource.RestApi), http.Actor()).ToHttp(),
                _ => Results.BadRequest("Mode must be manual or automatic.")
            });
        return app;
    }
}
