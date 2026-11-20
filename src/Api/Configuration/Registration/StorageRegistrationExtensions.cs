using Application;
using Infrastructure;

namespace Api.Configuration.Registration;

public static class StorageRegistrationExtensions
{
    public static WebApplicationBuilder AddStorageServices(this WebApplicationBuilder builder)
    {
        var provider = builder.Configuration["Storage:Provider"] ?? "Memory";
        if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = builder.Configuration.GetConnectionString("Scada");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Missing ConnectionStrings:Scada.");

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
            throw new InvalidOperationException("Unknown Storage:Provider.");
        }

        return builder;
    }
}
