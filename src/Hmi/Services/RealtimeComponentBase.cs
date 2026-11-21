using Application.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Hmi.Services;

/// <summary>
/// Base for live HMI components. Each component instance owns a bounded realtime subscription (oldest messages are dropped
/// when it falls behind) and re-reads its state at most every <see cref="MinimumRefreshInterval"/>.
/// </summary>
public abstract class RealtimeComponentBase : ComponentBase, IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private IRealtimeSubscription? _subscription;
    private Task? _pump;
    private CancellationToken _stoppingToken;

    [Inject] protected ILogger<RealtimeComponentBase> Logger { get; set; } = default!;
    protected string? RefreshError { get; private set; }

    [Inject] protected HmiSession Session { get; set; } = default!;
    [Inject] protected IRealtimeFeed Feed { get; set; } = default!;

    /// <summary>Topics that trigger a refresh; empty means the component does not update live.</summary>
    protected virtual IReadOnlyCollection<string> Topics => [];

    protected virtual TimeSpan MinimumRefreshInterval => TimeSpan.FromMilliseconds(250);

    /// <summary>Re-reads the component's state from the application services. Return true when it should re-render.</summary>
    protected abstract bool Refresh();

    /// <summary>Loads data from asynchronous services as part of a refresh.</summary>
    protected virtual Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected CancellationToken StoppingToken => _stoppingToken;

    /// <summary>Called for each received message before <see cref="Refresh"/>; for components that use the payload itself.</summary>
    protected virtual bool OnMessage(RealtimeMessage message) => false;

    protected override async Task OnInitializedAsync()
    {
        _stoppingToken = _stopping.Token;
        try
        {
            await Session.InitializeAsync();
            if (Topics.Count > 0)
                _subscription = Feed.Subscribe(Topics, capacity: 64);
            await TryRefreshAsync(StoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !StoppingToken.IsCancellationRequested)
        {
            ReportError(exception);
        }
        if (_subscription is not null)
            _pump = PumpAsync(_subscription, StoppingToken);
    }

    /// <summary>Re-reads state safely for button handlers.</summary>
    protected async Task<bool> RefreshNowAsync()
    {
        await InvokeAsync(async () =>
        {
            await TryRefreshAsync(StoppingToken);
            StateHasChanged();
        });
        return true;
    }

    private async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var changed = Refresh();
            await RefreshAsync(cancellationToken);
            var recovered = RefreshError is not null;
            RefreshError = null;
            return changed || recovered;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            return ReportError(exception);
        }
    }

    private bool ReportError(Exception exception)
    {
        Logger.LogWarning(exception, "Could not refresh HMI component {Component}", GetType().Name);
        RefreshError = "Live data could not be refreshed. Retrying on the next update.";
        return true;
    }

    private async Task PumpAsync(IRealtimeSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            while (await subscription.Reader.WaitToReadAsync(cancellationToken))
            {
                try
                {
                    await InvokeAsync(async () =>
                    {
                        while (subscription.Reader.TryRead(out var message))
                            OnMessage(message);
                        if (await TryRefreshAsync(cancellationToken))
                            StateHasChanged();
                    });
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    ReportError(exception);
                    await InvokeAsync(StateHasChanged);
                }
                await Task.Delay(MinimumRefreshInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public virtual async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        if (_subscription is not null)
            await _subscription.DisposeAsync();
        if (_pump is not null)
            await _pump;
        _stopping.Dispose();
        GC.SuppressFinalize(this);
    }
}
