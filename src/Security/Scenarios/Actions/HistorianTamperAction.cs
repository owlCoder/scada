using System.Globalization;

namespace Security.Scenarios.Actions;

/// <summary>Silently rewrites the latest historian sample of a tag, breaking its hash chain.</summary>
public sealed class HistorianTamperAction(string tagId, double value) : IScenarioAction
{
    public string Description => string.Create(CultureInfo.InvariantCulture, $"Tamper historian {tagId} = {value}");

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ScenarioActionResults.From(context.Training.TamperHistorian(tagId, value),
            $"Historian record for {tagId} altered", $"No historian sample for {tagId}"));
}
