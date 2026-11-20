using Api.Configuration.Registration;

namespace Api.Configuration;

public static class ServiceRegistrationExtensions
{
    public static WebApplicationBuilder AddScadaServices(this WebApplicationBuilder builder) =>
        builder.AddStorageServices()
            .AddApplicationServices()
            .AddSecurityServices()
            .AddProtocolServices()
            .AddSimulationServices();
}
