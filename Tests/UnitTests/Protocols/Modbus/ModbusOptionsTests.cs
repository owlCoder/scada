using Protocols.Modbus;
using Xunit;

namespace UnitTests.Protocols.Modbus;

public sealed class ModbusOptionsTests
{
    [Fact]
    public Task DefaultsAreValidAndBindToLoopback()
    {
        var options = new ModbusOptions();
        Assert.False(options.Enabled);
        Assert.Equal("127.0.0.1", options.BindAddress);
        Assert.Empty(options.Validate());
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("192.0.2.1")]
    public Task AcceptsIpv4BindAddress(string address)
    {
        Assert.Empty(new ModbusOptions { BindAddress = address }.Validate());
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("::1")]
    [InlineData("999.0.0.1")]
    public Task RejectsInvalidBindAddress(string address)
    {
        var error = Assert.Single(new ModbusOptions { BindAddress = address }.Validate());
        Assert.Contains("BindAddress", error);
        return Task.CompletedTask;
    }
}
