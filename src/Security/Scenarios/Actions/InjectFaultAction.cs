using Domain.Process.Commands;

namespace Security.Scenarios.Actions;

public sealed class InjectFaultAction(FaultInjection fault) : IScenarioAction
{
    public string Description =>
        $"Inject fault on {fault.DeviceId} (pump {fault.PumpFault}, valve {fault.ValveFault}, sensor {fault.SensorFault})";

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ScenarioActionResults.From(context.Training.InjectFault(fault)));
}
