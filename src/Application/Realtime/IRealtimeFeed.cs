namespace Application.Realtime;

public interface IRealtimeFeed
{
    /// <summary>Subscribes to the given topics (<see cref="RealtimeTopics"/>); an empty list means all topics.</summary>
    IRealtimeSubscription Subscribe(IReadOnlyCollection<string> topics, int capacity = 256);

    int SubscriberCount { get; }
}
