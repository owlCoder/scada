using System.Globalization;
using Application.Detection;
using Application.Events;
using Application.Grid;
using Domain.Grid.Snapshots;
using Domain.Incidents;
using Security.Detection;

namespace Security.Grid.Detection.Rules;

/// <summary>
/// Rejected grid commands that indicate an attack or misuse — NotAuthorized, Replay or RateLimited — grouped by actor
/// within the window (<c>Threshold</c> per actor, default 1), plus commands accepted from the field-protocol channel
/// (iec104) at an unusual rate (8 or more in the window), which is how a compromised protocol client looks.
/// </summary>
public sealed class GridUnauthorizedCommandRule : GridDetectionRuleBase
{
    private const int Iec104BurstCount = 8;
    private static readonly string[] SensitiveReasons = ["notauthorized", "not authorized", "replay", "ratelimited", "rate limit", "rate-limited"];

    private readonly Lock _sync = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _rejected = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _iec104 = new(StringComparer.Ordinal);

    public override string Id => "grid-unauthorized-command";
    public override string Name => "Unauthorized command";
    public override string Description => "Rejected grid commands (NotAuthorized, Replay, RateLimited) or an unusual command rate from the iec104 channel.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 1, 120, 300);

    public override int ResetState()
    {
        lock (_sync)
        {
            var cleared = _rejected.Values.Sum(q => q.Count) + _iec104.Values.Sum(q => q.Count);
            _rejected.Clear();
            _iec104.Clear();
            return cleared;
        }
    }

    public override IReadOnlyList<DetectionFinding> Evaluate(GridDetectionContext context, DetectionRuleSettings settings)
    {
        lock (_sync)
        {
            var findings = new List<DetectionFinding>();
            var windowStart = context.Now.AddSeconds(-settings.WindowSeconds);

            foreach (var stepEvent in context.Report.Events.Where(e => e.Kind == GridStepEventKind.CommandRejected
                && IsSensitive(e.Message)))
                Track(_rejected, stepEvent.Actor ?? "unknown", context.Now);

            foreach (var securityEvent in context.EventsWithin(settings, EventCategories.CommandRejected))
            {
                if (IsSensitive(securityEvent.Message))
                    Track(_rejected, securityEvent.Actor ?? "unknown", securityEvent.Timestamp);
            }

            foreach (var stepEvent in context.Report.Events.Where(e => e.Kind == GridStepEventKind.CommandAccepted
                && string.Equals(e.Channel, "iec104", StringComparison.OrdinalIgnoreCase)))
                Track(_iec104, stepEvent.Actor ?? "iec104", context.Now);

            Prune(_rejected, windowStart);
            Prune(_iec104, windowStart);

            var threshold = Math.Max(1, (int)Math.Round(settings.Threshold));
            foreach (var (actor, queue) in _rejected)
            {
                if (queue.Count < threshold)
                    continue;
                findings.Add(new($"rejected:{actor}", $"Unauthorized command attempts from {actor}",
                    $"{queue.Count} grid command(s) from {actor} were rejected as NotAuthorized, Replay or RateLimited within {settings.WindowSeconds}s.",
                    null,
                    [Evidence("actor", actor, string.Create(CultureInfo.InvariantCulture, $"{queue.Count} sensitive rejections in {settings.WindowSeconds}s"))]));
            }

            foreach (var (actor, queue) in _iec104)
            {
                if (queue.Count < Iec104BurstCount)
                    continue;
                findings.Add(new($"iec104:{actor}", $"Unusual command rate from iec104 actor {actor}",
                    $"{queue.Count} commands accepted from {actor} over the iec104 channel within {settings.WindowSeconds}s (usual burst {Iec104BurstCount}).",
                    null,
                    [Evidence("actor", actor, string.Create(CultureInfo.InvariantCulture, $"{queue.Count} iec104 commands in {settings.WindowSeconds}s"))]));
            }

            return findings;
        }
    }

    private static bool IsSensitive(string message) =>
        SensitiveReasons.Any(reason => message.Contains(reason, StringComparison.OrdinalIgnoreCase));

    private static int Track(Dictionary<string, Queue<DateTimeOffset>> target, string key, DateTimeOffset at)
    {
        var queue = target.TryGetValue(key, out var q) ? q : target[key] = new();
        queue.Enqueue(at);
        return queue.Count;
    }

    private static int Prune(Dictionary<string, Queue<DateTimeOffset>> target, DateTimeOffset windowStart)
    {
        var pruned = 0;
        foreach (var queue in target.Values)
            while (queue.Count > 0 && queue.Peek() < windowStart)
            {
                queue.Dequeue();
                pruned++;
            }
        return pruned;
    }
}
