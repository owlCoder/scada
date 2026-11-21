using Application.Grid;
using Microsoft.Extensions.DependencyInjection;
using Security.Grid.Detection;
using Security.Grid.Detection.Rules;
using Security.Grid.Scenarios;
using Security.Grid.Scenarios.Catalog;

namespace Api.Configuration.Registration;

/// <summary>
/// Grid-mode security services: the grid detection engine (an <see cref="IGridObserver"/>) with its rules, and the grid
/// scenario engine (an <see cref="IGridObserver"/> and the active-run provider for detection correlation). They resolve
/// <see cref="IGridView"/>/<see cref="IGridTraining"/>/<see cref="IGridControl"/> lazily, so they only need them once the
/// grid simulation is registered.
/// </summary>
public static class GridSecurityRegistrationExtensions
{
    public static WebApplicationBuilder AddGridSecurityServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;

        // Fallbacks so the service graph validates in hosts where the grid simulation (Agent A) is not wired up yet.
        // A real IGridView/IGridTraining/IGridControl registered anywhere in the chain overrides these (last wins).

        services.AddSingleton<IEnumerable<IGridDetectionRule>>(sp =>
            DefaultGridDetectionRules.Create(sp.GetRequiredService<IGridView>()));
        services.AddSingleton<GridDetectionEngine>();
        services.AddSingleton<GridDetectionRuleAdministrationService>();
        services.AddSingleton<IGridObserver>(sp => sp.GetRequiredService<GridDetectionEngine>());

        services.AddSingleton<IEnumerable<GridScenarioDefinition>>(_ => DefaultGridScenarios.Create());
        services.AddSingleton<GridScenarioEngine>();
        services.AddSingleton<IGridObserver>(sp => sp.GetRequiredService<GridScenarioEngine>());
        services.AddSingleton<IGridActiveScenarioProvider>(sp => sp.GetRequiredService<GridScenarioEngine>());

        services.AddHostedService<GridDetectionSettingsRecoveryService>();
        return builder;
    }
}
