using Application;
using Domain;
using Infrastructure;
using Protocols;
using Security;
using Simulation;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Plant>();

var provider = builder.Configuration["Storage:Provider"] ?? "Memory";
if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("Scada");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("Недостаје ConnectionStrings:Scada.");

    builder.Services.AddSingleton(Npgsql.NpgsqlDataSource.Create(connectionString));
    builder.Services.AddSingleton<IEventStore, PostgresEventStore>();
    builder.Services.AddSingleton<ITelemetryStore, PostgresTelemetryStore>();
}
else if (provider.Equals("Memory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
    builder.Services.AddSingleton<ITelemetryStore, InMemoryTelemetryStore>();
}
else
{
    throw new InvalidOperationException("Непознат Storage:Provider.");
}

builder.Services.AddSingleton<IPlantService, PlantService>();
builder.Services.AddSingleton<CommandMonitor>();
builder.Services.AddSingleton<ScenarioService>();
builder.Services.AddSingleton<PlantSnapshotService>();
builder.Services.AddSingleton<IncidentService>();
builder.Services.AddSingleton<VirtualRegisterService>();
builder.Services.AddSingleton<ModbusFrameService>();
if (builder.Configuration.GetValue<bool>("Protocols:ModbusEnabled"))
{
    var port = builder.Configuration.GetValue<int?>("Protocols:ModbusPort") ?? 1502;
    if (port is < 1024 or > 65535)
        throw new InvalidOperationException("Modbus порт мора бити између 1024 и 65535.");
    builder.Services.AddSingleton<IHostedService>(services =>
        new ModbusTcpService(services.GetRequiredService<ModbusFrameService>(),
            services.GetRequiredService<ILogger<ModbusTcpService>>(), port));
}
builder.Services.AddSingleton(new AccessPolicy(new Dictionary<AccessRole, string>
{
    [AccessRole.Viewer] = builder.Configuration["Security:ViewerApiKey"] ?? "",
    [AccessRole.Operator] = builder.Configuration["Security:OperatorApiKey"]
        ?? builder.Configuration["Security:CommandApiKey"] ?? "",
    [AccessRole.Administrator] = builder.Configuration["Security:AdminApiKey"] ?? ""
}));
builder.Services.AddHostedService<SimulationService>();
builder.Services.AddHostedService<IncidentMonitorService>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", async (IServiceProvider services, CancellationToken cancellationToken) =>
{
    var dataSource = services.GetService<Npgsql.NpgsqlDataSource>();
    if (dataSource is null)
        return Results.Ok(new { status = "healthy", storage = "memory" });

    try
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return Results.Ok(new { status = "healthy", storage = "postgres" });
    }
    catch (Npgsql.NpgsqlException)
    {
        return Results.Json(new { status = "degraded", storage = "postgres" }, statusCode: 503);
    }
});

var read = app.MapGroup("/api");
read.AddEndpointFilter(async (context, next) =>
{
    var policy = context.HttpContext.RequestServices.GetRequiredService<AccessPolicy>();
    var key = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
    return policy.Authorize(key, AccessRole.Viewer)
        ? await next(context)
        : Results.Unauthorized();
});

read.MapGet("/plant", (PlantSnapshotService snapshots) => Results.Ok(snapshots.GetSnapshot()));
read.MapGet("/events", (IEventStore store) => Results.Ok(store.GetRecent()));
read.MapGet("/telemetry", (int? count, ITelemetryStore store) =>
    count is < 1 or > 3600
        ? Results.BadRequest("Број узорака мора бити између 1 и 3600.")
        : Results.Ok(store.GetRecent(count ?? 100)));
read.MapGet("/incidents", (IncidentService service) => Results.Ok(service.GetAll()));
read.MapGet("/scenarios", (ScenarioService scenarios) => Results.Ok(scenarios.GetAvailable()));
read.MapGet("/registers", (VirtualRegisterService registers) => Results.Ok(registers.ReadAll()));
read.MapGet("/registers/{address:int}", (int address, VirtualRegisterService registers) =>
    address is >= 0 and <= ushort.MaxValue && registers.Read((ushort)address) is { } register
        ? Results.Ok(register)
        : Results.NotFound());

var commands = app.MapGroup("/api/plant");
commands.AddEndpointFilter(async (context, next) =>
{
    var policy = context.HttpContext.RequestServices.GetRequiredService<AccessPolicy>();
    var key = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
    return policy.Authorize(key, AccessRole.Operator)
        ? await next(context)
        : Results.Unauthorized();
});

commands.MapPost("/tick", (TickRequest request, IPlantService service, PlantSnapshotService snapshots) =>
{
    if (!double.IsFinite(request.Seconds) || request.Seconds is <= 0 or > 60)
        return Results.BadRequest("Корак мора бити између 0 и 60 секунди.");

    service.Advance(request.Seconds);
    return Results.Ok(snapshots.GetSnapshot());
});

commands.MapPost("/pump/{id}/command", (string id, PumpCommand command, IPlantService service,
    PlantSnapshotService snapshots, CommandMonitor monitor, HttpContext http) =>
{
    if (id != "P-101")
        return Results.NotFound();
    if (string.IsNullOrWhiteSpace(command.Actor))
        return Results.BadRequest("Оператер је обавезан.");
    if (!monitor.Check(ApiKeyValidator.Fingerprint(http.Request.Headers["X-Api-Key"].ToString())))
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);

    try
    {
        service.SetPump(command.Running, command.Actor);
        return Results.Ok(snapshots.GetSnapshot());
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(exception.Message);
    }
});

commands.MapPost("/valve/{id}/command", (string id, ValveCommand command, IPlantService service,
    PlantSnapshotService snapshots, CommandMonitor monitor, HttpContext http) =>
{
    if (id != "V-101")
        return Results.NotFound();
    if (string.IsNullOrWhiteSpace(command.Actor))
        return Results.BadRequest("Оператер је обавезан.");
    if (!monitor.Check(ApiKeyValidator.Fingerprint(http.Request.Headers["X-Api-Key"].ToString())))
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);

    service.SetOutletValve(command.Open, command.Actor);
    return Results.Ok(snapshots.GetSnapshot());
});

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
        return registers.Write((ushort)address, request.Value, request.Actor)
            ? Results.Ok(registers.Read((ushort)address))
            : Results.BadRequest("Регистар није уписив или вредност није дозвољена.");
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(exception.Message);
    }
}).AddEndpointFilter(async (context, next) =>
{
    var policy = context.HttpContext.RequestServices.GetRequiredService<AccessPolicy>();
    var key = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
    return policy.Authorize(key, AccessRole.Operator)
        ? await next(context)
        : Results.Unauthorized();
});

var incidentCommands = app.MapGroup("/api/incidents");
incidentCommands.AddEndpointFilter(async (context, next) =>
{
    var policy = context.HttpContext.RequestServices.GetRequiredService<AccessPolicy>();
    var key = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
    return policy.Authorize(key, AccessRole.Administrator)
        ? await next(context)
        : Results.Unauthorized();
});

app.MapPost("/api/scenarios/{id}", (string id, ScenarioRequest request, ScenarioService scenarios) =>
{
    if (string.IsNullOrWhiteSpace(request.Actor))
        return Results.BadRequest("Оператер је обавезан.");

    return scenarios.Execute(id, request.Actor)
        ? Results.Accepted($"/api/scenarios/{id}")
        : Results.NotFound();
}).AddEndpointFilter(async (context, next) =>
{
    var policy = context.HttpContext.RequestServices.GetRequiredService<AccessPolicy>();
    var key = context.HttpContext.Request.Headers["X-Api-Key"].ToString();
    return policy.Authorize(key, AccessRole.Administrator)
        ? await next(context)
        : Results.Unauthorized();
});

incidentCommands.MapPost("/{id:guid}/status", (Guid id, IncidentStatusRequest request,
    IncidentService service, IEventStore events) =>
{
    if (string.IsNullOrWhiteSpace(request.Actor) ||
        !Enum.IsDefined(request.Status))
        return Results.BadRequest("Неисправан статус или оператер.");

    try
    {
        if (!service.TryUpdate(id, request.Status, request.Actor, out var incident))
            return Results.NotFound();

        events.Append(new(DateTimeOffset.UtcNow, "Audit",
            $"Статус инцидента {id} промењен у {request.Status}", request.Actor));
        return Results.Ok(incident);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(exception.Message);
    }
});

app.Run();

public sealed record TickRequest(double Seconds);
public sealed record PumpCommand(bool Running, string Actor);
public sealed record ValveCommand(bool Open, string Actor);
public sealed record IncidentStatusRequest(IncidentStatus Status, string Actor);
public sealed record RegisterWrite(ushort Value, string Actor);
public sealed record ScenarioRequest(string Actor);
