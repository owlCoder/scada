using Application.Scenarios;

namespace Security.Scenarios;

public sealed record ScenarioControlResult(bool Succeeded, string Message, ScenarioRunRecord? Run)
{
    public static ScenarioControlResult Ok(string message, ScenarioRunRecord? run) => new(true, message, run);

    public static ScenarioControlResult Fail(string message, ScenarioRunRecord? run = null) => new(false, message, run);
}
