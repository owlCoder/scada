namespace Application.Events;

public interface IEventPublisher
{
    /// <summary>Publishes without blocking: the event is buffered for readers, queued for persistence and broadcast.</summary>
    bool Publish(SecurityEvent securityEvent);
}
