using System.Collections.Concurrent;
using Application.Events;

namespace Api.RateLimiting;

/// <summary>Audits rate-limit rejections, at most once per partition per minute so a flood does not flood the event log too.</summary>
public sealed class RateLimitAuditor(IEventPublisher events, TimeProvider time)
{
    private const int MaxTracked = 10_000;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAudit = new(StringComparer.Ordinal);

    public bool Record(string partition, HttpContext context)
    {
        var now = time.GetUtcNow();
        if (_lastAudit.TryGetValue(partition, out var last) && now - last < Interval)
            return false;
        if (_lastAudit.Count >= MaxTracked)
            _lastAudit.Clear();
        _lastAudit[partition] = now;
        return events.Publish(SecurityEvent.Create(now, EventCategories.SecurityAlert, EventSeverity.Medium, "api",
            $"Rate limit exceeded on {context.Request.Method} {context.Request.Path}", partition));
    }
}
