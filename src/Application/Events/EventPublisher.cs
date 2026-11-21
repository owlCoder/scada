using Application.Persistence;
using Application.Realtime;

namespace Application.Events;

public sealed class EventPublisher(RecentEventBuffer buffer, IPersistenceQueue persistence, IRealtimePublisher realtime)
    : IEventPublisher
{
    public bool Publish(SecurityEvent securityEvent)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);
        buffer.Add(securityEvent);
        var queued = persistence.TryEnqueue(new SecurityEventWorkItem(securityEvent));
        realtime.Publish(RealtimeTopics.Events, securityEvent);
        return queued;
    }
}
