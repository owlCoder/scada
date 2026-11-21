using Application.Events;
using Application.Realtime;
using Application.Simulation;
using Application.Telemetry;
using Infrastructure.Realtime;
using Security.Detection;
using Security.Scenarios;
using Simulation.Configuration;

namespace Api.Configuration.Registration;

public static class SimulationRegistrationExtensions
{
    public static WebApplicationBuilder AddSimulationServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var options = builder.Configuration.Bind<SimulationOptions>(ConfigurationSections.Simulation);
        var host = builder.Configuration.Bind<SimulationHostOptions>(ConfigurationSections.SimulationHost);
        var topology = PlantConfigurationLoader.Load(host.TopologyFile);
        var errors = options.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        services.AddSingleton(options);
        services.AddSingleton(host);
        services.AddSingleton(topology);
        services.AddSingleton<SimulationEngine>();
        services.AddSingleton<TelemetryHistorian>(_ => new TelemetryHistorian());
        services.AddSingleton<RecentEventBuffer>(_ => new RecentEventBuffer());
        services.AddSingleton<IEventPublisher, EventPublisher>();
        services.AddSingleton<IRealtimePublisher, NullRealtimePublisher>();

        // Order matters: scenario actions run before detection evaluates the same step.
        services.AddSingleton<ISimulationObserver>(sp => sp.GetRequiredService<ScenarioEngine>());
        services.AddSingleton<ISimulationObserver>(sp => sp.GetRequiredService<DetectionEngine>());
        services.AddSingleton<SimulationCoordinator>();
        return builder;
    }
}
