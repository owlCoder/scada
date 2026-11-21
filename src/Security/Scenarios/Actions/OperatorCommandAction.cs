using System.Globalization;
using Application.Commands;
using Application.Identity;

namespace Security.Scenarios.Actions;

/// <summary>
/// Issues a supervisory command through <see cref="ControlCommandService"/>. When <paramref name="sequenced"/> is set
/// the next valid sequence number is attached; <paramref name="captureAs"/> stores it for a later replay.
/// </summary>
public sealed class OperatorCommandAction(
    Actor actor,
    OperatorCommandType type,
    string deviceId,
    double? value = null,
    bool sequenced = false,
    string? captureAs = null,
    CommandSource source = CommandSource.RestApi) : IScenarioAction
{
    public string Description => string.Create(CultureInfo.InvariantCulture,
        $"{actor.Name} ({actor.Role}): {type} {deviceId}{(value is null ? "" : $" {value}")}{(sequenced ? " [sequenced]" : "")}");

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken)
    {
        long? sequence = sequenced || captureAs is not null ? context.NextSequence(actor.Name) : null;
        if (captureAs is not null && sequence is { } captured)
            context.Capture(captureAs, captured);

        var result = context.Commands.Execute(new OperatorCommand(deviceId, type, value, source, sequence), actor);
        return Task.FromResult(ScenarioActionResults.Issued(result));
    }
}
