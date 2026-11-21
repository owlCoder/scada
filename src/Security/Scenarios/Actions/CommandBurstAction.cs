using System.Globalization;
using Application.Commands;
using Application.Identity;

namespace Security.Scenarios.Actions;

/// <summary>Floods the command service with identical requests from one identity.</summary>
public sealed class CommandBurstAction(
    Actor actor,
    OperatorCommandType type,
    string deviceId,
    int count,
    double? value = null,
    CommandSource source = CommandSource.RestApi) : IScenarioAction
{
    public string Description => string.Create(CultureInfo.InvariantCulture, $"Burst of {count} x {type} {deviceId} from {actor.Name}");

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken)
    {
        var accepted = 0;
        var total = Math.Max(1, count);
        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            accepted += context.Commands.Execute(new OperatorCommand(deviceId, type, value, source), actor).Accepted ? 1 : 0;
        }

        return Task.FromResult(ScenarioActionResult.Done(string.Create(CultureInfo.InvariantCulture,
            $"{total} commands sent; {accepted} accepted, {total - accepted} rejected")));
    }
}
