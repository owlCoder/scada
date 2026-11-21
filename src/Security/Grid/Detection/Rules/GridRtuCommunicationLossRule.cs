using Application.Detection;
using Application.Grid;
using Domain.Grid.Snapshots;
using Domain.Incidents;
using Security.Detection;
using Domain.Process.Devices;

namespace Security.Grid.Detection.Rules;

/// <summary>
/// A whole RTU is unreachable: an <see cref="GridStepEventKind.RtuOnlineChanged"/> to offline, or every measurement the
/// RTU reports has <see cref="MeasurementQuality.Bad"/> quality.
/// </summary>
public sealed class GridRtuCommunicationLossRule : GridDetectionRuleBase
{
    public override string Id => "grid-rtu-communication-loss";
    public override string Name => "RTU communication loss";
    public override string Description => "An entire RTU is offline or all its measurements have Bad quality.";
    public override IncidentCategory Category => IncidentCategory.Communication;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 0, 60, 300);

    public override IReadOnlyList<DetectionFinding> Evaluate(GridDetectionContext context, DetectionRuleSettings settings)
    {
        var findings = new List<DetectionFinding>();
        var subjects = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var stepEvent in context.Report.Events.Where(e => e.Kind == GridStepEventKind.RtuOnlineChanged
            && e.Message.Contains("offline", StringComparison.OrdinalIgnoreCase)))
            subjects[stepEvent.SourceId] = stepEvent.Message;

        foreach (var group in context.Current.Measurements.GroupBy(m => m.RtuId, StringComparer.Ordinal))
        {
            var measurements = group.ToArray();
            if (measurements.Length > 0 && measurements.All(m => m.Quality == MeasurementQuality.Bad))
                subjects.TryAdd(group.Key, $"all {measurements.Length} measurements of RTU {group.Key} have Bad quality");
        }

        foreach (var (rtuId, detail) in subjects)
        {
            findings.Add(new(rtuId, $"RTU {rtuId} communication lost",
                $"No trustworthy telemetry from RTU {rtuId}: {detail}.", rtuId,
                [Evidence("rtu", rtuId, detail)]));
        }

        return findings;
    }
}
