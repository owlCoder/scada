using System.Globalization;

namespace Security.Scenarios.Actions;

/// <summary>Replaces a sensor's reported value (null clears the spoof). The physical process is unchanged.</summary>
public sealed class SpoofSensorAction(string sensorId, double? value) : IScenarioAction
{
    public string Description => value is null
        ? $"Clear spoof on {sensorId}"
        : string.Create(CultureInfo.InvariantCulture, $"Spoof {sensorId} = {value}");

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ScenarioActionResults.From(context.Training.SpoofSensor(sensorId, value)));
}
