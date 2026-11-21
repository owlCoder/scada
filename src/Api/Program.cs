using Api.Configuration;
using Api.Configuration.Registration;
using Api.Endpoints.Accounts;
using Api.Endpoints.Alarms;
using Api.Endpoints.Controllers;
using Api.Endpoints.Detections;
using Api.Endpoints.GridSecurity;
using Api.Endpoints.Events;
using Api.Endpoints.Identity;
using Api.Endpoints.Incidents;
using Api.Endpoints.Network;
using Api.Endpoints.Plant;
using Api.Endpoints.Registers;
using Api.Endpoints.Scenarios;
using Api.Endpoints.Simulation;
using Api.Endpoints.System;
using Api.Endpoints.Telemetry;
using Api.Development;
using Api.Endpoints.SignIn;
using Api.Middleware;
using Api.Hosting;
using Security.Accounts;

if (args is ["--healthcheck"])
{
    Environment.ExitCode = await ContainerHealthCheck.RunAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddScadaServices();

var app = builder.Build();
app.WarnIfOidcMetadataAllowsHttp();
app.UseScadaForwardedHeaders();
// The schema must be current before accounts are seeded or any store is read.
await app.InitializeStorageAsync(CancellationToken.None);
// Accounts must exist before the first request is authenticated.
await app.Services.GetRequiredService<AccountSeeder>().SeedAsync(CancellationToken.None);
await app.Services.GetRequiredService<DevelopmentAccountGenerator>().EnsureAsync(CancellationToken.None);
app.UseScadaObservability();
app.UseScadaSecurityHeaders();
app.UseAuthentication();
app.UseMiddleware<CookieAntiforgeryMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.MapStaticAssets();

HealthEndpoints.Map(app);
SignInEndpoints.Map(app);
IdentityEndpoints.Map(app);
PlantEndpoints.Map(app);
SimulationEndpoints.Map(app);
ControllerEndpoints.Map(app);
NetworkEndpoints.Map(app);
RegisterEndpoints.Map(app);
AlarmEndpoints.Map(app);
EventEndpoints.Map(app);
TelemetryEndpoints.Map(app);
IncidentEndpoints.Map(app);
ScenarioEndpoints.Map(app);
DetectionEndpoints.Map(app);
GridSecurityEndpoints.Map(app);
AccountEndpoints.Map(app);
app.MapScadaHmi();

app.Run();

public partial class Program;
