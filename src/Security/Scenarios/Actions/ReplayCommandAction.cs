using System.Globalization;
using Application.Commands;
using Application.Identity;

namespace Security.Scenarios.Actions;

/// <summary>Re-sends a previously captured command with its original (now stale) sequence number.</summary>
public sealed class ReplayCommandAction(
    Actor actor,
    OperatorCommandType type,
    string deviceId,
    string captureKey,
    int repetitions,
    double? value = null,
    CommandSource source = CommandSource.RestApi) : IScenarioAction
{
    public string Description => string.Create(CultureInfo.InvariantCulture,
        $"Replay captured '{captureKey}' ({type} {deviceId}) as {actor.Name} x{repetitions}");

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Recall(captureKey) is not { } sequence)
            return Task.FromResult(ScenarioActionResult.Failed($"No command was captured as '{captureKey}'."));

        var rejected = 0;
        for (var i = 0; i < Math.Max(1, repetitions); i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = context.Commands.Execute(new OperatorCommand(deviceId, type, value, source, sequence), actor);
            rejected += result.Accepted ? 0 : 1;
        }

        return Task.FromResult(ScenarioActionResult.Done(string.Create(CultureInfo.InvariantCulture,
            $"Replayed sequence {sequence} {Math.Max(1, repetitions)} time(s); {rejected} rejected")));
    }
}
