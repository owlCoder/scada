namespace ProtocolTests.Support;

// Stop polling after the initial connection poll and the handshake response.
// Unlike a blocked message callback, this applies real transport backpressure to the server.
internal sealed class StalledPollHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly TaskCompletionSource _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _polls;

    public Task Blocked => _blocked.Task;

    public bool Release() => _release.TrySetResult();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/hubs/scada" &&
            Interlocked.Increment(ref _polls) >= 3)
        {
            _blocked.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
