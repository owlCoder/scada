using Domain.Network;
using Xunit;

namespace UnitTests.Domain.Network;

public sealed class NetworkTests
{
    private static (long Delivered, long Lost) Transmit(int seed)
    {
        var network = new VirtualNetwork(DefaultNetworkDefinition.Create(), new SeededRandomSource(seed));
        network.FindLink("L-SCADA-PLC1")!.SetImpairment(20, 40);
        for (var i = 0; i < 500; i++)
            network.Transmit("L-SCADA-PLC1");
        var link = network.FindLink("L-SCADA-PLC1")!;
        return (link.Delivered, link.Lost);
    }

    [Fact]
    public Task ImpairmentIsDeterministicForAGivenSeed()
    {
        var first = Transmit(42);
        Assert.Equal(first, Transmit(42));
        Assert.InRange(first.Lost, 100, 300);
        Assert.NotEqual(first, Transmit(7));
        return Task.CompletedTask;
    }

    [Fact]
    public Task DisconnectedLinkDeliversNothingUntilReconnected()
    {
        var network = new VirtualNetwork(DefaultNetworkDefinition.Create(), new SeededRandomSource(1));
        Assert.True(network.FindLink("L-PLC1-IO")!.Disconnect());
        Assert.False(network.Transmit("L-PLC1-IO").Delivered);
        Assert.False(network.FindLink("L-PLC1-IO")!.Disconnect());
        Assert.True(network.FindLink("L-PLC1-IO")!.Reconnect());
        Assert.True(network.Transmit("L-PLC1-IO").Delivered);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("ENT", "CTL", false)]
    [InlineData("ENT", "FLD-1", false)]
    [InlineData("DMZ", "CTL", false)]
    [InlineData("SUP", "CTL", true)]
    [InlineData("CTL", "FLD-1", true)]
    [InlineData("ENT", "DMZ", true)]
    [InlineData("ENT", "NOPE", false)]
    public Task ConnectionsFollowTheZoneAndConduitModel(string source, string target, bool allowed)
    {
        var network = new VirtualNetwork(DefaultNetworkDefinition.Create(), new SeededRandomSource(1));
        var blocked = network.EvaluateConnection("src", source, "dst", target);
        Assert.Equal(allowed, blocked is null);
        Assert.Equal(allowed ? 0 : 1, network.BlockedAttempts);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RestoreDefaultsClearsImpairmentAndDisconnection()
    {
        var network = new VirtualNetwork(DefaultNetworkDefinition.Create(), new SeededRandomSource(1));
        network.FindLink("L-SCADA-PLC1")!.SetImpairment(500, 90);
        network.FindLink("L-PLC2-IO")!.Disconnect();

        Assert.Equal(2, network.RestoreDefaults());
        Assert.Equal(0, network.FindLink("L-SCADA-PLC1")!.PacketLossPercent);
        Assert.False(network.FindLink("L-PLC2-IO")!.IsDisconnected);
        return Task.CompletedTask;
    }
}
