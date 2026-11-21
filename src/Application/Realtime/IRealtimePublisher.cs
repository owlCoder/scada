namespace Application.Realtime;

public interface IRealtimePublisher
{
    /// <summary>Non-blocking broadcast. Slow subscribers lose their oldest messages instead of blocking the publisher.</summary>
    bool Publish(string topic, object payload);
}
