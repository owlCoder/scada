using Application.Alarms;
using Application.Detection;
using Application.Events;
using Application.Identity;
using Application.Incidents;
using Application.Persistence;
using Application.Scenarios;
using Application.Simulation;
using Application.Telemetry;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Persistence.Queue;

namespace Api.Configuration.Registration;

public static class StorageRegistrationExtensions
{
    public static WebApplicationBuilder AddStorageServices(this WebApplicationBuilder builder)
    {
        var provider = builder.Configuration[$"{ConfigurationSections.Storage}:Provider"] ?? "Memory";
        if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Scada")))
                throw new InvalidOperationException("Missing ConnectionStrings:Scada.");
            throw new InvalidOperationException(
                "PostgreSQL storage is provided by the EF Core persistence layer, which is not available in this build. Use Storage:Provider=Memory.");
        }

        if (!provider.Equals("Memory", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unknown Storage:Provider '{provider}'.");

        var services = builder.Services;
        services.AddSingleton<InMemoryEventStore>();
        services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<InMemoryEventStore>());
        services.AddSingleton<IRetentionTarget>(sp => sp.GetRequiredService<InMemoryEventStore>());
        services.AddSingleton<InMemoryTelemetryStore>();
        services.AddSingleton<ITelemetryStore>(sp => sp.GetRequiredService<InMemoryTelemetryStore>());
        services.AddSingleton<IRetentionTarget>(sp => sp.GetRequiredService<InMemoryTelemetryStore>());
        services.AddSingleton<InMemoryAlarmHistoryStore>();
        services.AddSingleton<IAlarmHistoryStore>(sp => sp.GetRequiredService<InMemoryAlarmHistoryStore>());
        services.AddSingleton<IRetentionTarget>(sp => sp.GetRequiredService<InMemoryAlarmHistoryStore>());
        services.AddSingleton<InMemoryDetectionStore>();
        services.AddSingleton<IDetectionStore>(sp => sp.GetRequiredService<InMemoryDetectionStore>());
        services.AddSingleton<IRetentionTarget>(sp => sp.GetRequiredService<InMemoryDetectionStore>());
        services.AddSingleton<IPlantStateStore, InMemoryPlantStateStore>();
        services.AddSingleton<IIncidentStore, InMemoryIncidentStore>();
        services.AddSingleton<IDetectionRuleSettingsStore, InMemoryDetectionRuleSettingsStore>();
        services.AddSingleton<IScenarioRunStore, InMemoryScenarioRunStore>();
        services.AddSingleton<IUserAccountStore, InMemoryUserAccountStore>();

        var queueOptions = builder.Configuration.Bind<PersistenceQueueOptions>(ConfigurationSections.Persistence);
        var errors = queueOptions.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
        services.AddSingleton(queueOptions);
        services.AddSingleton<ChannelPersistenceQueue>();
        services.AddSingleton<IPersistenceQueue>(sp => sp.GetRequiredService<ChannelPersistenceQueue>());
        services.AddSingleton<PersistenceDispatcher>();
        services.AddSingleton<PersistenceWorker>();
        return builder;
    }
}
