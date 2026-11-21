namespace Security.Scenarios.Actions;

public sealed class ControllerControlAction(string controllerId, ControllerOperation operation) : IScenarioAction
{
    public string Description => $"{operation} controller {controllerId}";

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken)
    {
        var succeeded = operation switch
        {
            ControllerOperation.Start => context.Training.SetControllerRunning(controllerId, true),
            ControllerOperation.Stop => context.Training.SetControllerRunning(controllerId, false),
            ControllerOperation.Reset => context.Training.ResetController(controllerId),
            _ => false
        };
        return Task.FromResult(ScenarioActionResults.From(succeeded, $"{controllerId}: {operation} done",
            $"{controllerId}: {operation} not applied"));
    }
}
