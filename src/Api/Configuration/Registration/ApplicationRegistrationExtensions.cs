using Application;
using Domain;
using Protocols;
using Security;

namespace Api.Configuration.Registration;

public static class ApplicationRegistrationExtensions
{
    public static WebApplicationBuilder AddApplicationServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<Plant>();
        builder.Services.AddSingleton<IPlantService, PlantService>();
        builder.Services.AddSingleton<CommandMonitor>();
        builder.Services.AddSingleton<ScenarioService>();
        builder.Services.AddSingleton<IncidentService>();
        builder.Services.AddSingleton<VirtualRegisterService>();
        builder.Services.AddSingleton<ModbusFrameService>();
        return builder;
    }
}
