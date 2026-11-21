using Application.Registers;
using Domain.Process.Commands;

namespace Security.Scenarios.Actions;

/// <summary>Maps service responses to action results. Rejections by the platform are reported, not treated as failures.</summary>
internal static class ScenarioActionResults
{
    public static ScenarioActionResult From(CommandResult result) => result.Accepted
        ? ScenarioActionResult.Done(result.Message)
        : ScenarioActionResult.Failed($"{result.Reason}: {result.Message}");

    public static ScenarioActionResult Issued(CommandResult result) => ScenarioActionResult.Done(result.Accepted
        ? $"accepted: {result.Message}"
        : $"rejected ({result.Reason}): {result.Message}");

    public static ScenarioActionResult Issued(RegisterWriteResult result) =>
        ScenarioActionResult.Done($"{result.Status}: {result.Message}");

    public static ScenarioActionResult From(bool succeeded, string done, string failed) =>
        succeeded ? ScenarioActionResult.Done(done) : ScenarioActionResult.Failed(failed);
}
