using System.Globalization;

namespace Security.Scenarios.Actions;

public sealed class SetTankLevelAction(string tankId, double levelPercent) : IScenarioAction
{
    public string Description => string.Create(CultureInfo.InvariantCulture, $"Set {tankId} level to {levelPercent}%");

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ScenarioActionResults.From(context.Training.SetTankLevel(tankId, levelPercent)));
}
