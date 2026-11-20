using Api.Contracts;
using Api.Endpoints;
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
    builder.Services.AddSingleton<IIncidentStore, PostgresIncidentStore>();
    builder.Services.AddSingleton<PostgresSchemaService>();
}
else if (provider.Equals("Memory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
    builder.Services.AddSingleton<ITelemetryStore, InMemoryTelemetryStore>();
    builder.Services.AddSingleton<IIncidentStore, InMemoryIncidentStore>();
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
var legacyPolicy = new AccessPolicy(new Dictionary<AccessRole, string>
{
    [AccessRole.Viewer] = builder.Configuration["Security:ViewerApiKey"] ?? "",
    [AccessRole.Operator] = builder.Configuration["Security:OperatorApiKey"]
        ?? builder.Configuration["Security:CommandApiKey"] ?? "",
    [AccessRole.Administrator] = builder.Configuration["Security:AdminApiKey"] ?? ""
});
builder.Services.AddSingleton(legacyPolicy);
var namedUsers = builder.Configuration.GetSection("Security:Users").GetChildren().Select(section =>
{
    var username = section["Username"] ?? "";
    var key = section["ApiKey"] ?? "";
    if (!Enum.TryParse<AccessRole>(section["Role"], true, out var role) ||
        !Enum.IsDefined(role))
        throw new InvalidOperationException($"Неисправна улога за корисника {username}.");
    return new UserCredential(username, role, key);
}).ToArray();
if (namedUsers.Any(user => legacyPolicy.Authenticate(user.ApiKey) is not null))
    throw new InvalidOperationException("Именовани корисник не сме користити дељени API кључ.");
builder.Services.AddSingleton(new UserAccessPolicy(namedUsers));
builder.Services.AddSingleton<AuthorizationService>();
builder.Services.AddHostedService<SimulationService>();
builder.Services.AddHostedService<IncidentMonitorService>();

var app = builder.Build();
if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    await app.Services.GetRequiredService<PostgresSchemaService>().VerifyAsync();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Cache-Control"] = "no-store";
    await next();
});
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

ReadEndpoints.Map(app);
PlantCommandEndpoints.Map(app);
RegisterCommandEndpoints.Map(app);
IncidentAndScenarioEndpoints.Map(app);

app.Run();








public partial class Program { }
