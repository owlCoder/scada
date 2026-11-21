using System.Globalization;
using Application.Detection;
using Application.Events;
using Domain.Incidents;
using Domain.Network;

namespace Security.Detection.Rules;

/// <summary>
/// Lost controller communication, links that are down, or packet loss above <c>Threshold</c> percent over
/// the last <c>WindowSeconds</c> simulated seconds.
/// </summary>
public sealed class CommunicationFailureRule : DetectionRuleBase
{
    private readonly Dictionary<string, Queue<(long Sent, long Delivered)>> _history = new(StringComparer.Ordinal);

    public override string Id => "communication-failure";
    public override string Name => "Communication failure";
    public override string Description => "Controller communication loss, link outage or excessive packet loss.";
    public override IncidentCategory Category => IncidentCategory.Communication;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 30, 10, 300);

    public override IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings)
    {
        var findings = new List<DetectionFinding>();
        var capacity = Math.Max(2, (int)Math.Round(settings.WindowSeconds / context.StepSeconds));
        foreach (var link in context.Report.Network.Links)
        {
            var history = _history.TryGetValue(link.Id, out var h) ? h : _history[link.Id] = new();
            history.Enqueue((link.Sent, link.Delivered));
            while (history.Count > capacity)
                history.Dequeue();

            if (link.State == LinkState.Down)
            {
                findings.Add(new(link.Id, $"Link {link.Id} is down", $"{link.Name} is disconnected.", link.TargetNode,
                    [Evidence("link", link.Id, "State Down")]));
                continue;
            }

            var (firstSent, firstDelivered) = history.Peek();
            var sent = link.Sent - firstSent;
            if (history.Count < capacity || sent < 4)
                continue;

            var loss = 100.0 * (sent - (link.Delivered - firstDelivered)) / sent;
            if (loss >= settings.Threshold)
                findings.Add(new(link.Id, $"Excessive packet loss on {link.Id}",
                    string.Create(CultureInfo.InvariantCulture, $"{loss:0.#}% of {sent} transmissions on {link.Name} failed."),
                    link.TargetNode,
                    [Evidence("link", link.Id, string.Create(CultureInfo.InvariantCulture, $"Loss {loss:0.#}%, latency {link.LatencyMilliseconds:0} ms"))]));
        }

        foreach (var scan in context.Report.Scans.Where(s => s.CommunicationLost))
            findings.Add(new($"{scan.ControllerId}:field", $"{scan.ControllerId} lost field communication",
                $"{scan.ControllerId} could not reach its field I/O and timed out.", scan.ControllerId,
                [Evidence("controller", scan.ControllerId, "Field communication offline")]));

        foreach (var change in context.Report.SupervisoryChanges.Where(c => c.Current == Domain.Control.CommunicationState.Offline))
            findings.Add(new($"{change.ControllerId}:supervisory", $"Supervisory link to {change.ControllerId} offline",
                $"The SCADA server can no longer poll {change.ControllerId}.", change.ControllerId,
                [Evidence("controller", change.ControllerId, $"{change.Previous} -> {change.Current}")]));

        return findings;
    }
}
