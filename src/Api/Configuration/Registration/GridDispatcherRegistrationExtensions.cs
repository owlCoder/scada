using Application.Grid;
using Microsoft.Extensions.DependencyInjection;
using Security.Grid.Control;

namespace Api.Configuration.Registration;

/// <summary>
/// Dispatcher-backend grid services: the operator tag store (<see cref="IGridTagging"/>, enforced by
/// <c>GridControlService</c> on every supervisory command incl. IEC 60870-5-104) and the reported-measurement
/// historian (<see cref="IGridHistory"/>). Both are <see cref="IGridObserver"/>s so they clear their state when a
/// different network definition is activated.
/// </summary>
public static class GridDispatcherRegistrationExtensions
{
    public static WebApplicationBuilder AddGridDispatcherServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddSingleton<GridTaggingService>();
        services.AddSingleton<IGridTagging>(sp => sp.GetRequiredService<GridTaggingService>());
        services.AddSingleton<IGridObserver>(sp => sp.GetRequiredService<GridTaggingService>());
        services.AddSingleton<GridHistoryService>();
        services.AddSingleton<IGridHistory>(sp => sp.GetRequiredService<GridHistoryService>());
        services.AddSingleton<IGridObserver>(sp => sp.GetRequiredService<GridHistoryService>());
        return builder;
    }
}
