using Application;
using Domain;
using Infrastructure;
using Security;
using Simulation;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Plant>();
builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
builder.Services.AddSingleton<IPlantService, PlantService>();
builder.Services.AddSingleton<CommandMonitor>();
builder.Services.AddHostedService<SimulationService>();

var app = builder.Build();

app.MapGet("/api/plant", (IPlantService service) => Results.Ok(service.GetState()));
app.MapGet("/api/events", (IEventStore store) => Results.Ok(store.GetRecent()));
app.MapPost("/api/plant/tick", (TickRequest request, IPlantService service) =>
{
    if (request.Seconds is <= 0 or > 60)
        return Results.BadRequest("Корак мора бити између 0 и 60 секунди.");
    service.Advance(request.Seconds);
    return Results.Ok(service.GetState());
});
app.MapPost("/api/plant/pump/{id}/command", (string id, PumpCommand command, IPlantService service, CommandMonitor monitor) =>
{
    if (id != "P-101")
        return Results.NotFound();
    if (string.IsNullOrWhiteSpace(command.Actor))
        return Results.BadRequest("Оператер је обавезан.");
    if (!monitor.Check(command.Actor))
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    service.SetPump(command.Running, command.Actor);
    return Results.Ok(service.GetState());
});

app.Run();

public sealed record TickRequest(double Seconds);
public sealed record PumpCommand(bool Running, string Actor);
