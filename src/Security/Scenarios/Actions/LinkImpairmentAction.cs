using System.Globalization;

namespace Security.Scenarios.Actions;

public sealed class LinkImpairmentAction(string linkId, double latencyMilliseconds, double packetLossPercent) : IScenarioAction
{
    public string Description => string.Create(CultureInfo.InvariantCulture,
        $"Impair link {linkId}: latency {latencyMilliseconds} ms, loss {packetLossPercent}%");

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ScenarioActionResults.From(
            context.Training.SetLinkImpairment(linkId, latencyMilliseconds, packetLossPercent),
            $"Link {linkId} impaired", $"Link {linkId} unknown or impairment out of range"));
}
