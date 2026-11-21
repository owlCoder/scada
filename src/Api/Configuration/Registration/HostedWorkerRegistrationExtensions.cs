using Infrastructure.Persistence.Queue;
using Infrastructure.Persistence.Retention;
using Protocols.Modbus;
using Simulation.HostedServices;

namespace Api.Configuration.Registration;

public static class HostedWorkerRegistrationExtensions
{
    /// <summary>Hosted services start in registration order: storage, recovery, then the simulation loop.</summary>
    public static WebApplicationBuilder AddHostedWorkers(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddHostedService(sp => sp.GetRequiredService<PersistenceWorker>());
        services.AddHostedService(sp => sp.GetRequiredService<RetentionWorker>());
        services.AddHostedService<DetectionSettingsRecoveryService>();
        services.AddHostedService<SimulationRecoveryService>();
        services.AddHostedService<SimulationService>();
        services.AddHostedService<InitialScenarioService>();
        if (services.Any(d => d.ServiceType == typeof(ModbusTcpServer)))
            services.AddHostedService(sp => sp.GetRequiredService<ModbusTcpServer>());
        return builder;
    }
}
