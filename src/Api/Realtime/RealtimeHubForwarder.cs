using Application.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Api.Realtime;

/// <summary>Each hub connection has a bounded feed and its own send loop, so transport backpressure stays local.</summary>
public sealed class RealtimeHubForwarder(IRealtimeFeed feed, IHubContext<ScadaHub> hub, ILogger<RealtimeHubForwarder> logger) : IHostedService
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, HubClientSubscription> _connections = [];
    private bool _stopping;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public bool Connect(string connectionId, bool instructor, CancellationToken connectionAborted)
    {
        lock (_sync)
        {
            if (_stopping || _connections.ContainsKey(connectionId))
                return false;
            var subscription = feed.Subscribe(RealtimeTopics.All, capacity: 256);
            _connections.Add(connectionId, new HubClientSubscription(subscription, hub.Clients.Client(connectionId),
                instructor, connectionAborted, logger));
            return true;
        }
    }

    public async Task<bool> DisconnectAsync(string connectionId)
    {
        HubClientSubscription? connection;
        lock (_sync)
            _connections.Remove(connectionId, out connection);
        if (connection is null)
            return false;
        await connection.DisposeAsync();
        return true;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        HubClientSubscription[] connections;
        lock (_sync)
        {
            _stopping = true;
            connections = _connections.Values.ToArray();
            _connections.Clear();
        }
        await Task.WhenAll(connections.Select(connection => connection.DisposeAsync().AsTask())).WaitAsync(cancellationToken);
    }
}
