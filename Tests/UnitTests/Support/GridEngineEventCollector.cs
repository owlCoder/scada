using Application.Events;

namespace UnitTests.Support;

public sealed class GridEngineEventCollector : IEventPublisher
{
    public List<SecurityEvent> Items { get; } = [];
    public bool Publish(SecurityEvent securityEvent) { Items.Add(securityEvent); return true; }
}
