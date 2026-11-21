using Application.Detection;

namespace Security.Detection;

/// <summary>
/// Groups related detections into one incident: everything during a scenario run correlates to that run,
/// otherwise detections of the same category on the same device (or rule) correlate together.
/// </summary>
public static class DetectionCorrelator
{
    public static string CorrelationKey(DetectionRecord detection) =>
        detection.ScenarioRunId is { } run
            ? $"scenario:{run:N}"
            : $"{detection.Category}:{detection.DeviceId ?? detection.RuleId}";
}
