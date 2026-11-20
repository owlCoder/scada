namespace Application;

public interface IEventStore
{
    void Append(SecurityEvent securityEvent);
    IReadOnlyList<SecurityEvent> GetRecent();
}
