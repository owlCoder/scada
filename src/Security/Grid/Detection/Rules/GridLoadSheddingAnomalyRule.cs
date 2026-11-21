using System.Globalization;
using Application.Detection;
using Application.Grid;
using Domain.Grid.Snapshots;
using Domain.Incidents;
using Security.Detection;

namespace Security.Grid.Detection.Rules;

/// <summary>
/// Loads shed without a legitimate operator command: a <see cref="GridStepEventKind.LoadShed"/> that is neither a
/// protection trip nor backed by an accepted command from an operator console (hmi/rest channel). Additionally, many
/// customers lost within the window without any protection trip (<c>Threshold</c> customers, default 100).
/// </summary>
public sealed class GridLoadSheddingAnomalyRule : GridDetectionRuleBase
{
    private static readonly string[] OperatorChannels = ["hmi", "rest"];

    private readonly Lock _sync = new();
    private readonly Queue<DateTimeOffset> _trips = new();

    public override string Id => "grid-load-shedding-anomaly";
    public override string Name => "Load shedding anomaly";
    public override string Description => "Loads shed without an operator command, or many customers lost without a protection trip.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 100, 60, 300);

    public override int ResetState()
    {
        lock (_sync)
        {
            var cleared = _trips.Count;
            _trips.Clear();
            return cleared;
        }
    }

    public override IReadOnlyList<DetectionFinding> Evaluate(GridDetectionContext context, DetectionRuleSettings settings)
    {
        lock (_sync)
        {
            var findings = new List<DetectionFinding>();
            var evented = new HashSet<string>(StringComparer.Ordinal);

            foreach (var stepEvent in context.Report.Events.Where(e => e.Kind == GridStepEventKind.LoadShed))
            {
                evented.Add(stepEvent.SourceId);
                if (stepEvent.Cause == GridChangeCause.Protection)
                    continue;
                if (stepEvent.Cause == GridChangeCause.Operator && IsOperatorConsoleCommand(context, stepEvent.SourceId, stepEvent.Actor))
                    continue;

                findings.Add(new($"shed:{stepEvent.SourceId}", $"Load {stepEvent.SourceId} shed without authorization",
                    $"{stepEvent.Message} (cause {stepEvent.Cause}, actor {stepEvent.Actor ?? "none"}).",
                    stepEvent.SourceId,
                    [Evidence("event", stepEvent.SourceId, stepEvent.Message)]));
            }

            foreach (var load in context.Current.Loads)
            {
                var before = context.Previous?.Load(load.Id);
                if (before is not null && !before.Shed && load.Shed && !evented.Contains(load.Id))
                    findings.Add(new($"shed:{load.Id}", $"Load {load.Name} shed without authorization",
                        $"{load.Id} ({load.Name}) is now shed with no matching shed event.", load.Id,
                        [Evidence("load", load.Id, $"{load.Customers} customers disconnected")]));
            }

            // Aggregate: many customers currently without supply and no protection trip in the window to explain it.
            foreach (var stepEvent in context.Report.Events.Where(e => e.Kind == GridStepEventKind.ProtectionTripped))
                _trips.Enqueue(context.Now);
            var windowStart = context.Now.AddSeconds(-settings.WindowSeconds);
            while (_trips.Count > 0 && _trips.Peek() < windowStart)
                _trips.Dequeue();

            var threshold = Math.Max(1, (int)Math.Round(settings.Threshold));
            var lost = context.Current.Totals.Customers - context.Current.Totals.CustomersSupplied;
            if (_trips.Count == 0 && lost >= threshold)
            {
                findings.Add(new("customers", "Many customers lost without a protection trip",
                    $"{lost} customers are without supply and no protection tripped within {settings.WindowSeconds}s.",
                    null,
                    [Evidence("totals", "customers", string.Create(CultureInfo.InvariantCulture, $"customers lost {lost}, threshold {threshold}"))]));
            }

            return findings;
        }
    }

    private static bool IsOperatorConsoleCommand(GridDetectionContext context, string loadId, string? actor)
    {
        return context.Report.Events.Any(e => e.Kind == GridStepEventKind.CommandAccepted
            && e.SourceId == loadId
            && OperatorChannels.Contains(e.Channel ?? "", StringComparer.OrdinalIgnoreCase)
            && (actor is null || e.Actor is null || string.Equals(e.Actor, actor, StringComparison.Ordinal)));
    }
}
