namespace Application;

public interface IEventStore
{
    bool Append(SecurityEvent securityEvent);
    IReadOnlyList<SecurityEvent> GetRecent();
}
