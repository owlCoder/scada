using System.Text.Json.Serialization;
using Api.Configuration.Registration;

namespace Api.Configuration;

public static class ServiceRegistrationExtensions
{
    public static WebApplicationBuilder AddScadaServices(this WebApplicationBuilder builder)
    {
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddSingleton(TimeProvider.System);
        return builder.AddScadaObservability()
            .AddStorageServices()
            .AddSimulationServices()
            .AddApplicationServices()
            .AddSecurityServices()
            .AddScadaAuthentication()
            .AddScadaOidc()
            .AddScadaAuthorization()
            .AddScadaRateLimiting()
            .AddScadaHmi()
            .AddProtocolServices()
            .AddHostedWorkers();
    }
}
