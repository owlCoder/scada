using Application;
using Infrastructure;
using Xunit;

namespace UnitTests.Persistence;

public sealed class AppendResultTests
{
    [Fact]
    public Task EventAppendReturnsSuccessWhenStored()
    {
        IEventStore store = new InMemoryEventStore();
        var item = new SecurityEvent(DateTimeOffset.UtcNow, "Audit", "Test event", "operator");

        Assert.True(store.Append(item));
        Assert.Contains(store.GetRecent(), entry => entry.Message == item.Message);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TelemetryAppendReturnsSuccessWhenStored()
    {
        ITelemetryStore store = new InMemoryTelemetryStore();
        var sample = new TelemetrySample(DateTimeOffset.UtcNow, 50, false, 1);

        Assert.True(store.Append(sample));
        Assert.Single(store.GetRecent(1));
        return Task.CompletedTask;
    }
}
