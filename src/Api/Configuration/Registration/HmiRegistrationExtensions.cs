using Api.Authentication;
using Api.Authorization;
using Api.Realtime;
using Hmi.Components;
using Hmi.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace Api.Configuration.Registration;

/// <summary>Blazor HMI (interactive server rendering) and the SignalR hub. Every HMI page requires at least the Viewer role.</summary>
public static class HmiRegistrationExtensions
{
    public const string HubPath = "/hubs/scada";

    public static WebApplicationBuilder AddScadaHmi(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, ScadaRevalidatingAuthenticationStateProvider>();
        services.AddScoped<HmiSession>();
        services.AddSignalR();
        services.AddSingleton<RealtimeHubForwarder>();
        services.AddHostedService(sp => sp.GetRequiredService<RealtimeHubForwarder>());
        return builder;
    }

    public static WebApplication MapScadaHmi(this WebApplication app)
    {
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode().RequireAuthorization(ScadaPolicies.Viewer);
        app.MapHub<ScadaHub>(HubPath);
        return app;
    }
}
