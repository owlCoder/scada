namespace Application.Realtime;

public sealed record RealtimeMessage(string Topic, object Payload, DateTimeOffset PublishedAt);
