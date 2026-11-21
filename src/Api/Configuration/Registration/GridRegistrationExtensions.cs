using Application.Grid;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Persistence.Postgres.Stores;
using Security.Grid.Control;
using Simulation.HostedServices;

namespace Api.Configuration.Registration;

public static class GridRegistrationExtensions
{
    public static WebApplicationBuilder AddGridServices(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.Bind<GridOptions>("Grid");
        var errors = options.Validate();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        var services = builder.Services;
        services.AddSingleton(options);
        services.AddSingleton<GridEngine>();
        services.AddSingleton<IGridView>(sp => sp.GetRequiredService<GridEngine>());
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<GridCoordinator>(sp, new DeferredServices<IGridObserver>(sp)));
        services.AddSingleton<IGridRuntime>(sp => sp.GetRequiredService<GridCoordinator>());
        services.AddSingleton<IGridDefinitionAdministration>(sp => sp.GetRequiredService<GridCoordinator>());
        services.AddSingleton<IGridControl, GridControlService>();
        services.AddSingleton<IGridTraining, GridTrainingService>();
        if (string.Equals(builder.Configuration["Storage:Provider"], "Postgres", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IGridDefinitionStore, PostgresGridDefinitionStore>();
        else services.AddSingleton<IGridDefinitionStore, InMemoryGridDefinitionStore>();
        services.AddHostedService<GridSimulationService>();
        return builder;
    }
}
