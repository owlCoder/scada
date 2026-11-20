using Application;
using Domain;
using Infrastructure;
using Security;
using Simulation;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Plant>();
builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
builder.Services.AddSingleton<ITelemetryStore, InMemoryTelemetryStore>();
builder.Services.AddSingleton<IPlantService, PlantService>();
builder.Services.AddSingleton<CommandMonitor>();
builder.Services.AddSingleton<PlantSnapshotService>();
builder.Services.AddHostedService<SimulationService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/api/plant", (PlantSnapshotService snapshots) => Results.Ok(snapshots.GetSnapshot()));
app.MapGet("/api/events", (IEventStore store) => Results.Ok(store.GetRecent()));
app.MapGet("/api/telemetry", (int? count, ITelemetryStore store) =>
    count is < 1 or > 3600
        ? Results.BadRequest("Број узорака мора бити између 1 и 3600.")
        : Results.Ok(store.GetRecent(count ?? 100)));

var commands = app.MapGroup("/api/plant");
commands.AddEndpointFilter(async (context, next) =>
{
    var configuredKey = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Security:CommandApiKey"];
    if (string.IsNullOrWhiteSpace(configuredKey))
        return Results.Problem("Команде су онемогућене док није подешен API кључ.", statusCode: 503);

    var suppliedKey = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
    if (!ApiKeyValidator.Matches(configuredKey, suppliedKey))
        return Results.Unauthorized();

    return await next(context);
});

commands.MapPost("/tick", (TickRequest request, IPlantService service, PlantSnapshotService snapshots) =>
{
    if (!double.IsFinite(request.Seconds) || request.Seconds is <= 0 or > 60)
        return Results.BadRequest("Корак мора бити између 0 и 60 секунди.");

    service.Advance(request.Seconds);
    return Results.Ok(snapshots.GetSnapshot());
});

commands.MapPost("/pump/{id}/command", (string id, PumpCommand command, IPlantService service, PlantSnapshotService snapshots, CommandMonitor monitor) =>
{
    if (id != "P-101")
        return Results.NotFound();
    if (string.IsNullOrWhiteSpace(command.Actor))
        return Results.BadRequest("Оператер је обавезан.");
    if (!monitor.Check(command.Actor))
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);

    service.SetPump(command.Running, command.Actor);
    return Results.Ok(snapshots.GetSnapshot());
});

app.Run();

public sealed record TickRequest(double Seconds);
public sealed record PumpCommand(bool Running, string Actor);
