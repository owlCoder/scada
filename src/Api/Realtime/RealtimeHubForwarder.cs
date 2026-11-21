using Application.Realtime;
using Application.Simulation;
using Domain.Process.Snapshots;
using Microsoft.AspNetCore.SignalR;

namespace Api.Realtime;

/// <summary>
/// Subscribes to the realtime feed with its own bounded queue and forwards messages to hub clients. Plant snapshots go to
/// instructors unchanged and to everyone else redacted, so true values and spoofing never reach operators.
/// </summary>
public sealed class RealtimeHubForwarder(IRealtimeFeed feed, IHubContext<ScadaHub> hub, ILogger<RealtimeHubForwarder> logger) : BackgroundService
{
    private static readonly string[] Everyone = [ScadaHubGroups.Instructors, ScadaHubGroups.Operators];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var subscription = feed.Subscribe([], capacity: 1024);
        try
        {
            await foreach (var message in subscription.Reader.ReadAllAsync(stoppingToken))
                await ForwardAsync(message, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Realtime hub forwarder stopped ({Dropped} message(s) dropped)", subscription.Dropped);
        }
    }

    public async Task<bool> ForwardAsync(RealtimeMessage message, CancellationToken cancellationToken)
    {
        try
        {
            if (message.Payload is PlantSnapshot plant)
            {
                await hub.Clients.Group(ScadaHubGroups.Instructors).SendAsync(ScadaHub.MessageMethod, message.Topic, plant, cancellationToken);
                await hub.Clients.Group(ScadaHubGroups.Operators)
                    .SendAsync(ScadaHub.MessageMethod, message.Topic, PlantViewRedactor.ForOperators(plant), cancellationToken);
            }
            else
            {
                await hub.Clients.Groups(Everyone).SendAsync(ScadaHub.MessageMethod, message.Topic, message.Payload, cancellationToken);
            }

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not forward realtime message on {Topic}", message.Topic);
            return false;
        }
    }
}
