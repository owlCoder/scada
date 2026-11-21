using System.Globalization;
using Application.Identity;

namespace Security.Scenarios.Actions;

/// <summary>Acknowledges every active unacknowledged alarm without looking at it (alarm flooding / complacency).</summary>
public sealed class AlarmAcknowledgeBurstAction(Actor actor) : IScenarioAction
{
    public string Description => $"{actor.Name} acknowledges all alarms";

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken)
    {
        var alarms = context.Alarms.GetCurrent();
        var acknowledged = alarms.Count(alarm => context.Alarms.Acknowledge(alarm.Key, actor));
        return Task.FromResult(ScenarioActionResult.Done(string.Create(CultureInfo.InvariantCulture,
            $"{acknowledged} of {alarms.Count} alarm(s) acknowledged")));
    }
}
