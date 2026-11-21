using Domain.Process.Commands;

namespace Hmi.Services;

public sealed record ActionOutcome(bool Succeeded, string Message)
{
    public static ActionOutcome From(Domain.Grid.Commands.GridCommandResult result) => new(result.Accepted, result.Message);
    public static ActionOutcome From(CommandResult result) => new(result.Accepted, result.Message);
    public static ActionOutcome Done(string message) => new(true, message);
    public static ActionOutcome Failed(string message) => new(false, message);
}
