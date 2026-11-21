namespace Security.Scenarios.Actions;

/// <summary>A connection attempt evaluated against the zone and conduit model; blocked attempts emit a Network event.</summary>
public sealed class ConnectionAttemptAction(string sourceNode, string sourceSegmentId, string targetNode, string targetSegmentId)
    : IScenarioAction
{
    public string Description => $"Connect {sourceNode} ({sourceSegmentId}) -> {targetNode} ({targetSegmentId})";

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken)
    {
        var blocked = context.Training.AttemptConnection(sourceNode, sourceSegmentId, targetNode, targetSegmentId);
        return Task.FromResult(ScenarioActionResult.Done(blocked is null ? "Connection permitted" : $"Blocked: {blocked.Reason}"));
    }
}
