using Application.Scenarios;

namespace Security.Scenarios;

public static class ScenarioOutcomeEvaluator
{
    /// <summary>
    /// A completed run is Detected when every expected rule fired, PartiallyDetected when some did and Missed when
    /// none did. Runs that were stopped or reset are Aborted regardless of what fired.
    /// </summary>
    public static ScenarioOutcome Evaluate(ScenarioRunState state, IReadOnlyCollection<string> expected, IReadOnlyCollection<string> observed)
    {
        if (state != ScenarioRunState.Completed)
            return ScenarioOutcome.Aborted;
        if (expected.Count == 0)
            return ScenarioOutcome.Detected;

        var matched = expected.Count(e => observed.Contains(e, StringComparer.Ordinal));
        return matched == expected.Count ? ScenarioOutcome.Detected
            : matched > 0 ? ScenarioOutcome.PartiallyDetected
            : ScenarioOutcome.Missed;
    }
}
