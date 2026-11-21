using Application.Realtime;

namespace Infrastructure.Realtime;

/// <summary>Discards realtime messages. Used until a push channel (SignalR) is configured.</summary>
public sealed class NullRealtimePublisher : IRealtimePublisher
{
    public bool Publish(string topic, object payload) => false;
}
