using Api.Configuration;
using Api.Endpoints.Commands;
using Api.Endpoints.Read;
using Api.Endpoints.System;
using Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddScadaServices();

var app = builder.Build();
if ((builder.Configuration["Storage:Provider"] ?? "Memory").Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    await app.Services.GetRequiredService<PostgresSchemaService>().VerifyAsync();

app.UseScadaSecurityHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();

HealthEndpoints.Map(app);
IdentityEndpoints.Map(app);
PlantReadEndpoints.Map(app);
EventReadEndpoints.Map(app);
IncidentReadEndpoints.Map(app);
ScenarioReadEndpoints.Map(app);
RegisterReadEndpoints.Map(app);
PlantCommandEndpoints.Map(app);
RegisterCommandEndpoints.Map(app);
IncidentEndpoints.Map(app);
ScenarioEndpoints.Map(app);

app.Run();

public partial class Program { }
