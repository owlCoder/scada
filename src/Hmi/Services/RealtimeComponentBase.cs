using Application.Realtime;
using Microsoft.AspNetCore.Components;

namespace Hmi.Services;

/// <summary>
/// Base for live HMI components. Each component instance owns a bounded realtime subscription (oldest messages are dropped
/// when it falls behind) and re-reads its state at most every <see cref="MinimumRefreshInterval"/>.
/// </summary>
public abstract class RealtimeComponentBase : ComponentBase, IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private IRealtimeSubscription? _subscription;

    [Inject] protected HmiSession Session { get; set; } = default!;
    [Inject] protected IRealtimeFeed Feed { get; set; } = default!;

    /// <summary>Topics that trigger a refresh; empty means the component does not update live.</summary>
    protected virtual IReadOnlyCollection<string> Topics => [];

    protected virtual TimeSpan MinimumRefreshInterval => TimeSpan.FromMilliseconds(250);

    /// <summary>Re-reads the component's state from the application services. Return true when it should re-render.</summary>
    protected abstract bool Refresh();

    /// <summary>Called for each received message before <see cref="Refresh"/>; for components that use the payload itself.</summary>
    protected virtual bool OnMessage(RealtimeMessage message) => false;

    protected override async Task OnInitializedAsync()
    {
        await Session.InitializeAsync();
        Refresh();
        if (Topics.Count > 0)
        {
            _subscription = Feed.Subscribe(Topics, capacity: 64);
            _ = PumpAsync(_subscription, _stopping.Token);
        }
    }

    /// <summary>Runs an action and re-renders, for button handlers that change state.</summary>
    protected async Task<bool> RefreshNowAsync()
    {
        await InvokeAsync(() =>
        {
            Refresh();
            StateHasChanged();
        });
        return true;
    }

    private async Task<bool> PumpAsync(IRealtimeSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            while (await subscription.Reader.WaitToReadAsync(cancellationToken))
            {
                while (subscription.Reader.TryRead(out var message))
                    OnMessage(message);
                await InvokeAsync(() =>
                {
                    if (Refresh())
                        StateHasChanged();
                });
                await Task.Delay(MinimumRefreshInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        if (_subscription is not null)
            await _subscription.DisposeAsync();
        _stopping.Dispose();
        GC.SuppressFinalize(this);
    }
}
