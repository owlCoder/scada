using Application.Commands;
using Application.Identity;
using Domain.Process.Devices;

namespace Security.Scenarios.Actions;

/// <summary>
/// Cleanup step: hands a device left in manual back to automatic control. Devices already in automatic are left
/// untouched so cleanup does not generate audit noise of its own.
/// </summary>
public sealed class RestoreAutomaticModeAction(string deviceId) : IScenarioAction
{
    private static readonly Actor Engine = Actor.System("scenario-engine");

    public string Description => $"Restore {deviceId} to automatic";

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken)
    {
        var plant = context.Training.Snapshot();
        var mode = plant.Pump(deviceId)?.Mode ?? plant.Valve(deviceId)?.Mode;
        if (mode is null)
            return Task.FromResult(ScenarioActionResult.Failed($"Unknown actuator {deviceId}."));
        if (mode == ControlMode.Automatic)
            return Task.FromResult(ScenarioActionResult.Done($"{deviceId} already automatic"));

        var result = context.Commands.Execute(
            new OperatorCommand(deviceId, OperatorCommandType.SetAutomatic, null, CommandSource.Scenario), Engine);
        return Task.FromResult(ScenarioActionResults.From(result));
    }
}
