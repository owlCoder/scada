using Hmi.Services;

namespace FrontendTests.Support;

public sealed class RealtimeProbeComponent : RealtimeComponentBase
{
    public CancellationToken LifetimeToken => StoppingToken;
    protected override bool Refresh() => true;
}
