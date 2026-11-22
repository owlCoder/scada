using Hmi.Services.Shell;

namespace Api.Configuration.Registration;

/// <summary>Dispatcher shell services, scoped per Blazor circuit: shared shell state and the mode-aware alarm monitor.</summary>
public static class ShellHmiRegistrationExtensions
{
    public static IServiceCollection AddScadaShellHmi(this IServiceCollection services)
    {
        services.AddScoped<ShellState>();
        services.AddScoped<ShellAlarmMonitor>();
        return services;
    }
}
