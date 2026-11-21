using Api.Authorization;
using Application.Identity;
using Application.Simulation;
using Application.Training;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Security.Scenarios;

namespace Api.Realtime;

/// <summary>
/// Realtime push for external clients (<c>/hubs/scada</c>). Clients receive <c>Realtime(topic, payload)</c> messages and can
/// call <see cref="GetSnapshot"/> once after connecting for the initial state. Plant data is redacted for non-instructors.
/// </summary>
[Authorize(Policy = ScadaPolicies.Viewer)]
public sealed class ScadaHub(IPlantView plant, SimulationEngine engine, ScenarioEngine scenarios, TimeProvider time,
    RealtimeHubForwarder forwarder) : Hub
{
    public const string MessageMethod = "Realtime";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, IsInstructor() ? ScadaHubGroups.Instructors : ScadaHubGroups.Operators);
        forwarder.Connect(Context.ConnectionId, IsInstructor(), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await forwarder.DisconnectAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public HubSnapshot GetSnapshot()
    {
        var snapshot = plant.Snapshot();
        return new HubSnapshot(IsInstructor() ? snapshot : PlantViewRedactor.ForOperators(snapshot), engine.Status, scenarios.Current,
            time.GetUtcNow());
    }

    private bool IsInstructor() => Context.User is { } user && MinimumRoleHandler.RoleOf(user) is AccessRole.Administrator;
}
