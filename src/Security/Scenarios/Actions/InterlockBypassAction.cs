namespace Security.Scenarios.Actions;

public sealed class InterlockBypassAction(bool bypassed, string actor) : IScenarioAction
{
    public string Description => $"{(bypassed ? "Bypass" : "Restore")} safety interlocks as {actor}";

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ScenarioActionResults.From(context.Training.SetInterlockBypass(bypassed, actor)));
}
