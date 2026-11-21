namespace Security.Scenarios.Actions;

public sealed class LinkStateAction(string linkId, bool connected) : IScenarioAction
{
    public string Description => $"{(connected ? "Reconnect" : "Disconnect")} link {linkId}";

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ScenarioActionResults.From(context.Training.SetLinkConnected(linkId, connected),
            $"Link {linkId} {(connected ? "connected" : "disconnected")}", $"Link {linkId} state unchanged or unknown"));
}
