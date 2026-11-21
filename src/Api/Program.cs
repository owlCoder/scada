using Api.Configuration;
using Api.Configuration.Registration;
using Api.Endpoints.Accounts;
using Api.Endpoints.Alarms;
using Api.Endpoints.Controllers;
using Api.Endpoints.Detections;
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
using Security.Accounts;

var builder = WebApplication.CreateBuilder(args);
builder.AddScadaServices();

var app = builder.Build();
// The schema must be current before accounts are seeded or any store is read.
await app.InitializeStorageAsync(CancellationToken.None);
// Accounts must exist before the first request is authenticated.
await app.Services.GetRequiredService<AccountSeeder>().SeedAsync(CancellationToken.None);
app.UseScadaSecurityHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();

HealthEndpoints.Map(app);
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
AccountEndpoints.Map(app);

app.Run();

public partial class Program;
