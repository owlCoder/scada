using Application.Commands;
using Application.Detection;
using Application.Common;
using Application.Grid;
using System.Security.Claims;
using Hmi.Services.Grid;
using Application.Identity;
using Application.Persistence;
using Application.Realtime;
using Application.Registers;
using Application.Training;
using Bunit;
using Hmi.Services;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Security.Detection;
using Security.Grid.Detection;
using Security.Grid.Detection.Rules;
using Security.Grid.Scenarios;
using Security.Grid.Scenarios.Catalog;
using UnitTests.Support;

namespace FrontendTests.Support;

/// <summary>
/// A bUnit context wired to the real application services of a <see cref="TestRig"/> (in-memory stores, fake clock) and a
/// signed-in user with the given role. Policies follow the role hierarchy, as in the API host.
/// </summary>
public sealed class HmiTestHost : IAsyncDisposable
{
    private HmiTestHost(AccessRole role, string name, RegisterWriteOptions? registerOptions, SimulationMode mode)
    {
        RealtimeBroadcaster? broadcaster = null;
        Rig = new TestRig(registerOptions, createRealtime: time => broadcaster = new RealtimeBroadcaster(time));
        Broadcaster = broadcaster!;
        var authorization = Context.AddAuthorization();
        authorization.SetAuthorized(name);
        authorization.SetRoles(role.ToString());
        authorization.SetClaims(new Claim("scada:mode", mode.ToString()));
        authorization.SetPolicies(Enum.GetValues<AccessRole>().Where(r => r <= role).Select(r => r.ToString()).ToArray());

        var services = Context.Services;
        services.AddSingleton(Rig.Engine);
        services.AddSingleton(Rig.Coordinator);
        services.AddSingleton(Rig.Commands);
        services.AddSingleton(Rig.Alarms);
        services.AddSingleton(Rig.Incidents);
        services.AddSingleton(Rig.Scenarios);
        services.AddSingleton(Rig.Detection);
        services.AddSingleton(Rig.Accounts);
        services.AddSingleton(Rig.Registers);
        services.AddSingleton(Rig.RegisterOptions);
        services.AddSingleton(Rig.Historian);
        services.AddSingleton(Rig.Buffer);
        services.AddSingleton<Application.Events.IEventStore>(Rig.EventStore);
        services.AddSingleton<Application.Persistence.IPersistenceQueue>(Rig.Queue);
        services.AddSingleton(Rig.Training);
        services.AddSingleton<IPlantView>(Rig.Training);
        services.AddSingleton<IStorageProbe, InMemoryStorageProbe>();
        var gridRuleSettings = new InMemoryDetectionRuleSettingsStore();
        services.AddSingleton<IDetectionRuleSettingsStore>(gridRuleSettings);
        services.AddSingleton<DetectionRuleAdministrationService>();
        services.AddSingleton<InstructorTrainingService>();
        services.AddSingleton<Application.Events.IEventPublisher>(Rig.Events);
        services.AddSingleton<TimeProvider>(Rig.Time);
        services.AddSingleton<IRealtimeFeed>(Broadcaster);
        GridControl = new HmiFakeGridControl(Grid);
        GridStore = new HmiFakeGridDefinitionStore(Grid, Rig.Time);
        GridAdministration = new HmiFakeGridDefinitionAdministration(GridStore);
        services.AddSingleton<IGridView>(Grid);
        services.AddSingleton<IGridControl>(GridControl);
        services.AddSingleton(new Application.Grid.GridOptions());
        services.AddSingleton<IGridTraining>(GridTraining);
        services.AddSingleton<IGridRuntime>(GridRuntime);
        services.AddSingleton<IGridDefinitionStore>(GridStore);
        services.AddSingleton<IGridDefinitionAdministration>(GridAdministration);
        services.AddSingleton<GridEquipmentCatalog>();
        // The real grid security stack over the fake grid services, so the grid scenarios/rules pages can be tested.
        GridScenarios = new GridScenarioEngine(DefaultGridScenarios.Create(), Grid, GridTraining, GridControl,
            Rig.DetectionStore, Rig.RunStore, Rig.Events, Broadcaster, Rig.Time, new CommandReplayGuard(),
            NullLogger<GridScenarioEngine>.Instance);
        GridDetection = new GridDetectionEngine(DefaultGridDetectionRules.Create(Grid), Rig.Buffer, Rig.DetectionStore,
            Rig.Incidents, Rig.Events, Broadcaster, GridScenarios, Grid, NullLogger<GridDetectionEngine>.Instance);
        services.AddSingleton(GridScenarios);
        services.AddSingleton(GridDetection);
        services.AddSingleton(new GridDetectionRuleAdministrationService(GridDetection, gridRuleSettings, Rig.Events, Rig.Time,
            NullLogger<GridDetectionRuleAdministrationService>.Instance));
        services.AddScoped<HmiSession>();
        services.AddScoped<Hmi.Services.Shell.ShellState>();
        services.AddScoped<Hmi.Services.Shell.ShellAlarmMonitor>();
    }

    public HmiFakeGridView Grid { get; } = new();
    public HmiFakeGridControl GridControl { get; }
    public HmiFakeGridTraining GridTraining { get; } = new();
    public HmiFakeGridRuntime GridRuntime { get; } = new();
    public HmiFakeGridDefinitionStore GridStore { get; }
    public HmiFakeGridDefinitionAdministration GridAdministration { get; }
    public GridScenarioEngine GridScenarios { get; }
    public GridDetectionEngine GridDetection { get; }
    public BunitContext Context { get; } = new();
    public TestRig Rig { get; }
    public RealtimeBroadcaster Broadcaster { get; }

    public static HmiTestHost Create(AccessRole role, string name = "tester", RegisterWriteOptions? registerOptions = null, SimulationMode mode = SimulationMode.Water) =>
        new(role, name, registerOptions, mode);

    public ValueTask DisposeAsync() => Context.DisposeAsync();
}
