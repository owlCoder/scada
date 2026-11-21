using Domain.Control;

namespace Security.Scenarios.Actions;

/// <summary>Forces a controller register so it overrides the live value, as a rogue engineering workstation would.</summary>
public sealed class ForceRegisterAction(string controllerId, RegisterArea area, ushort address, ushort value) : IScenarioAction
{
    public string Description => $"Force {controllerId} {(area == RegisterArea.Holding ? "HR" : "IR")}{address} = {value}";

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ScenarioActionResults.From(context.Training.ForceRegister(controllerId, area, address, value),
            $"{controllerId}:{area}:{address} forced to {value}", $"Register {controllerId}:{area}:{address} could not be forced"));
}
