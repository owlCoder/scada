using Application.Alarms;
using Application.Commands;
using Application.Incidents;
using Application.Registers;
using Application.Training;

namespace Api.Configuration.Registration;

public static class ApplicationRegistrationExtensions
{
    public static WebApplicationBuilder AddApplicationServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddSingleton(builder.Configuration.Bind<RegisterWriteOptions>(ConfigurationSections.Registers));
        services.AddSingleton<CommandReplayGuard>();
        services.AddSingleton<ControlCommandService>();
        services.AddSingleton<RegisterAccessService>();
        services.AddSingleton<AlarmService>();
        services.AddSingleton<IncidentService>();
        services.AddSingleton<TrainingEnvironment>();
        services.AddSingleton<ITrainingEnvironment>(sp => sp.GetRequiredService<TrainingEnvironment>());
        services.AddSingleton<IPlantView>(sp => sp.GetRequiredService<TrainingEnvironment>());
        return builder;
    }
}
