using System.Threading.Channels;

namespace Application.Realtime;

/// <summary>A subscriber's own bounded queue. When the subscriber falls behind, its oldest messages are dropped.</summary>
public interface IRealtimeSubscription : IAsyncDisposable
{
    ChannelReader<RealtimeMessage> Reader { get; }

    /// <summary>Messages dropped because this subscriber did not keep up.</summary>
    long Dropped { get; }
}
