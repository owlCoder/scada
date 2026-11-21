using Application.Events;
using Application.Realtime;
using Application.Simulation;
using Domain.Process.Snapshots;
using Microsoft.AspNetCore.SignalR;

namespace Api.Realtime;

internal sealed class HubClientSubscription : IAsyncDisposable
{
    private readonly IRealtimeSubscription _subscription;
    private readonly CancellationTokenSource _stop;
    private readonly Task _pump;

    public HubClientSubscription(IRealtimeSubscription subscription, IClientProxy client, bool instructor,
        CancellationToken connectionAborted, ILogger logger)
    {
        _subscription = subscription;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(connectionAborted);
        _pump = PumpAsync(client, instructor, logger);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _pump;
        _stop.Dispose();
    }

    private async Task PumpAsync(IClientProxy client, bool instructor, ILogger logger)
    {
        try
        {
            await foreach (var message in _subscription.Reader.ReadAllAsync(_stop.Token))
            {
                // Training events (injected faults, spoofing, tampering) are for instructors only.
                if (!instructor && message.Payload is SecurityEvent { Category: EventCategories.Scenario })
                    continue;
                var payload = !instructor && message.Payload is PlantSnapshot plant
                    ? PlantViewRedactor.ForOperators(plant) : message.Payload;
                await client.SendAsync(ScadaHub.MessageMethod, message.Topic, payload, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            logger.LogDebug("Realtime hub connection stopped ({Dropped} message(s) dropped)", _subscription.Dropped);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not forward realtime messages to hub connection");
        }
        finally
        {
            await _subscription.DisposeAsync();
        }
    }
}
