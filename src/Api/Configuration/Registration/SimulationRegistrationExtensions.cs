using Simulation;

namespace Api.Configuration.Registration;

public static class SimulationRegistrationExtensions
{
    public static WebApplicationBuilder AddSimulationServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddHostedService<SimulationService>();
        builder.Services.AddHostedService<IncidentMonitorService>();
        return builder;
    }
}
