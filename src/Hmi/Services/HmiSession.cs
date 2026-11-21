using System.Security.Claims;
using Application.Identity;
using Application.Common;
using Application.Grid;
using Domain.Grid.Snapshots;
using Application.Simulation;
using Application.Training;
using Domain.Process.Snapshots;
using Microsoft.AspNetCore.Components.Authorization;

namespace Hmi.Services;

/// <summary>
/// The signed-in user of one HMI circuit. Instructors (Administrators) see ground truth; everyone else sees only what the
/// field instruments report, exactly as the REST API does.
/// </summary>
public sealed class HmiSession(AuthenticationStateProvider authentication, IPlantView plant, IGridView? grid = null)
{
    private bool _initialized;
    public SimulationMode Mode { get; private set; } = SimulationMode.Water;
    public IGridView? GridView => grid;

    public Actor Actor { get; private set; } = new("unknown", AccessRole.Viewer);
    public bool CanOperate => Actor.HasRole(AccessRole.Operator);
    public bool IsInstructor => Actor.HasRole(AccessRole.Administrator);

    public async Task<bool> InitializeAsync()
    {
        if (_initialized)
            return false;
        _initialized = true;
        Apply(await authentication.GetAuthenticationStateAsync());
        authentication.AuthenticationStateChanged += task => _ = UpdateAsync(task);
        return true;
    }

    public PlantSnapshot Plant() => IsInstructor ? plant.Snapshot() : PlantViewRedactor.ForOperators(plant.Snapshot());

    public GridSnapshot GridSnapshot()
    {
        var snapshot = (grid ?? throw new InvalidOperationException("Grid services are not available.")).Snapshot();
        return IsInstructor ? snapshot : GridViewRedactor.ForOperators(snapshot);
    }

    private async Task<bool> UpdateAsync(Task<AuthenticationState> state)
    {
        Apply(await state);
        return true;
    }

    private bool Apply(AuthenticationState state)
    {
        var user = state.User;
        Mode = Enum.TryParse<SimulationMode>(user.FindFirst("scada:mode")?.Value, out var mode) && Enum.IsDefined(mode)
            ? mode : SimulationMode.Water;
        var role = Enum.TryParse<AccessRole>(user.FindFirst(ClaimTypes.Role)?.Value, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : AccessRole.Viewer;
        Actor = new Actor(user.Identity?.IsAuthenticated == true ? user.Identity.Name ?? "unknown" : "unknown", role);
        return true;
    }
}
