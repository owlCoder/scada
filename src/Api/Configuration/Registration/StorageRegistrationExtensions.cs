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
using Infrastructure.Persistence.Postgres;
using Infrastructure.Persistence.Postgres.Stores;
using Infrastructure.Persistence.Queue;
using Infrastructure.Persistence.Retention;
using Microsoft.EntityFrameworkCore;

namespace Api.Configuration.Registration;

/// <summary>
/// <c>Storage:Provider</c> selects <c>Memory</c>/<c>InMemory</c> (default) or <c>Postgres</c> (EF Core, needs <c>ConnectionStrings:Scada</c>).
/// With Postgres, pending migrations are applied at startup unless <c>Storage:ApplyMigrations</c> is false.
/// </summary>
public static class StorageRegistrationExtensions
{
    public static WebApplicationBuilder AddStorageServices(this WebApplicationBuilder builder)
    {
        var provider = builder.Configuration[$"{ConfigurationSections.Storage}:Provider"] ?? "Memory";
        if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            AddPostgres(builder);
        else if (provider.Equals("Memory", StringComparison.OrdinalIgnoreCase)
            || provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
            AddInMemory(builder.Services);
        else
            throw new InvalidOperationException($"Unknown Storage:Provider '{provider}'.");

        AddPersistenceQueue(builder);
        AddRetention(builder);
        return builder;
    }

    /// <summary>Applies database migrations when PostgreSQL storage is configured. Call before anything reads the stores.</summary>
    public static async Task<IReadOnlyList<string>> InitializeStorageAsync(this WebApplication app, CancellationToken cancellationToken)
    {
        var migrator = app.Services.GetService<ScadaDatabaseMigrator>();
        var apply = app.Configuration.GetValue($"{ConfigurationSections.Storage}:ApplyMigrations", true);
        return migrator is not null && apply ? await migrator.MigrateAsync(cancellationToken) : [];
    }

    private static IServiceCollection AddPostgres(WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("Scada");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Storage:Provider=Postgres requires ConnectionStrings:Scada.");

        var services = builder.Services;
        services.AddPooledDbContextFactory<ScadaDbContext>(options => ScadaDbContext.Configure(options, connectionString));
        services.AddSingleton<ScadaDatabaseMigrator>();
        services.AddSingleton<IStorageProbe, PostgresStorageProbe>();
        AddRetainedStore<IEventStore, PostgresEventStore>(services);
        AddRetainedStore<ITelemetryStore, PostgresTelemetryStore>(services);
        AddRetainedStore<IAlarmHistoryStore, PostgresAlarmHistoryStore>(services);
        AddRetainedStore<IDetectionStore, PostgresDetectionStore>(services);
        services.AddSingleton<IPlantStateStore, PostgresPlantStateStore>();
        services.AddSingleton<IIncidentStore, PostgresIncidentStore>();
        services.AddSingleton<IDetectionRuleSettingsStore, PostgresDetectionRuleSettingsStore>();
        services.AddSingleton<IScenarioRunStore, PostgresScenarioRunStore>();
        services.AddSingleton<IUserAccountStore, PostgresUserAccountStore>();
        return services;
    }

    private static IServiceCollection AddInMemory(IServiceCollection services)
    {
        services.AddSingleton<IStorageProbe, InMemoryStorageProbe>();
        AddRetainedStore<IEventStore, InMemoryEventStore>(services);
        AddRetainedStore<ITelemetryStore, InMemoryTelemetryStore>(services);
        AddRetainedStore<IAlarmHistoryStore, InMemoryAlarmHistoryStore>(services);
        AddRetainedStore<IDetectionStore, InMemoryDetectionStore>(services);
        services.AddSingleton<IPlantStateStore, InMemoryPlantStateStore>();
        services.AddSingleton<IIncidentStore, InMemoryIncidentStore>();
        services.AddSingleton<IDetectionRuleSettingsStore, InMemoryDetectionRuleSettingsStore>();
        services.AddSingleton<IScenarioRunStore, InMemoryScenarioRunStore>();
        services.AddSingleton<IUserAccountStore, InMemoryUserAccountStore>();
        return services;
    }

    private static IServiceCollection AddRetainedStore<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService, IRetentionTarget
    {
        services.AddSingleton<TImplementation>();
        services.AddSingleton<TService>(sp => sp.GetRequiredService<TImplementation>());
        services.AddSingleton<IRetentionTarget>(sp => sp.GetRequiredService<TImplementation>());
        return services;
    }

    private static IServiceCollection AddPersistenceQueue(WebApplicationBuilder builder)
    {
        var options = builder.Configuration.Bind<PersistenceQueueOptions>(ConfigurationSections.Persistence);
        ThrowIfInvalid(options.Validate());
        var services = builder.Services;
        services.AddSingleton(options);
        services.AddSingleton<ChannelPersistenceQueue>();
        services.AddSingleton<IPersistenceQueue>(sp => sp.GetRequiredService<ChannelPersistenceQueue>());
        services.AddSingleton<PersistenceDispatcher>();
        services.AddSingleton<PersistenceWorker>();
        return services;
    }

    private static IServiceCollection AddRetention(WebApplicationBuilder builder)
    {
        var options = builder.Configuration.Bind<RetentionOptions>(ConfigurationSections.Retention);
        ThrowIfInvalid(options.Validate());
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<RetentionWorker>();
        return builder.Services;
    }

    private static bool ThrowIfInvalid(IReadOnlyList<string> errors) =>
        errors.Count == 0 ? true : throw new InvalidOperationException(string.Join(" ", errors));
}
