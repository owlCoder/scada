using Application.Diagnostics;
using Application.Persistence;
using Application.Realtime;

namespace Application.Events;

public sealed class EventPublisher(RecentEventBuffer buffer, IPersistenceQueue persistence, IRealtimePublisher realtime)
    : IEventPublisher
{
    public bool Publish(SecurityEvent securityEvent)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        if (securityEvent.CorrelationId is null && ScadaInstrumentation.CurrentCorrelationId is { } correlationId)
            securityEvent = securityEvent with { CorrelationId = correlationId };
        ScadaInstrumentation.Events.Add(1, new KeyValuePair<string, object?>("category", securityEvent.Category));
        buffer.Add(securityEvent);
        var queued = persistence.TryEnqueue(new SecurityEventWorkItem(securityEvent));
        realtime.Publish(RealtimeTopics.Events, securityEvent);
        return queued;
    }
}
