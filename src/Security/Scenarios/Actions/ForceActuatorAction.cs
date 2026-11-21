using System.Globalization;

namespace Security.Scenarios.Actions;

/// <summary>Drives a pump (value &gt;= 0.5 means on) or valve (percent) outside every legitimate control path.</summary>
public sealed class ForceActuatorAction(string deviceId, double value) : IScenarioAction
{
    public const string Actor = "unattributed";

    public string Description => string.Create(CultureInfo.InvariantCulture, $"Force {deviceId} = {value}");

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ScenarioActionResults.From(context.Training.ForceActuator(deviceId, value, Actor)));
}
