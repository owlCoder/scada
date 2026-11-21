using Application.Identity;
using Protocols.Iec104;
using Xunit;

namespace UnitTests.Protocols.Iec104;

public sealed class Iec104OptionsTests
{
    [Fact]
    public Task DefaultsAreValidAndBindToLoopback()
    {
        var options = new Iec104Options();
        Assert.False(options.Enabled);
        Assert.Equal("127.0.0.1", options.BindAddress);
        Assert.Equal(2404, options.Port);
        Assert.Equal(4, options.MaxConnections);
        Assert.Equal(12, options.K);
        Assert.Equal(8, options.W);
        Assert.Equal(30, options.T0);
        Assert.Equal(15, options.T1);
        Assert.Equal(10, options.T2);
        Assert.Equal(20, options.T3);
        Assert.Empty(options.AllowedClients);
        Assert.True(options.CommandsEnabled);
        Assert.Equal("iec104-master", options.CommandActor);
        Assert.Equal(AccessRole.Operator, options.CommandRole);
        Assert.Empty(options.Validate());
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("192.0.2.1")]
    public Task AcceptsIpv4BindAddress(string address)
    {
        Assert.Empty(new Iec104Options { BindAddress = address }.Validate());
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("::1")]
    [InlineData("999.0.0.1")]
    public Task RejectsInvalidBindAddress(string address)
    {
        var error = Assert.Single(new Iec104Options { BindAddress = address }.Validate());
        Assert.Contains("BindAddress", error);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(2404)]
    [InlineData(1024)]
    [InlineData(65535)]
    public Task AcceptsValidPorts(int port)
    {
        Assert.Empty(new Iec104Options { Port = port }.Validate());
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(0)]
    public Task RejectsInvalidPorts(int port)
    {
        Assert.Contains(new Iec104Options { Port = port }.Validate(), e => e.Contains("port"));
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(12, 8)]
    [InlineData(32767, 32767)]
    public Task AcceptsValidKAndW(int k, int w)
    {
        Assert.Empty(new Iec104Options { K = k, W = w }.Validate());
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(12, 13)]
    [InlineData(32768, 1)]
    public Task RejectsInvalidKAndW(int k, int w)
    {
        Assert.NotEmpty(new Iec104Options { K = k, W = w }.Validate());
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public Task RejectsOutOfRangeTimers(int seconds)
    {
        var errors = new Iec104Options { T0 = seconds, T1 = seconds, T2 = seconds, T3 = seconds }.Validate();
        Assert.Equal(4, errors.Count);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("192.0.2.10")]
    [InlineData("10.0.0.5", "127.0.0.1", "0.0.0.0")]
    public Task AcceptsIpv4AllowedClients(params string[] clients)
    {
        Assert.Empty(new Iec104Options { AllowedClients = [.. clients] }.Validate());
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("::1")]
    [InlineData("master.example")]
    [InlineData("999.1.1.1")]
    public Task RejectsNonIpv4AllowedClients(string client)
    {
        var error = Assert.Single(new Iec104Options { AllowedClients = [client] }.Validate());
        Assert.Contains("AllowedClients", error);
        return Task.CompletedTask;
    }

    [Fact]
    public Task RejectsEmptyCommandActorAndViewerRole()
    {
        var actor = Assert.Single(new Iec104Options { CommandActor = " " }.Validate());
        Assert.Contains("CommandActor", actor);
        var role = Assert.Single(new Iec104Options { CommandRole = AccessRole.Viewer }.Validate());
        Assert.Contains("CommandRole", role);
        Assert.Empty(new Iec104Options { CommandRole = AccessRole.Administrator }.Validate());
        return Task.CompletedTask;
    }
}
