using Application;
using Domain;
using Infrastructure;
using Protocols;
using Xunit;

namespace UnitTests.Protocols;

public sealed class ModbusFrameTests
{
    private static ModbusFrameService Create() =>
        new(new VirtualRegisterService(
            new PlantService(new Plant(), new InMemoryEventStore(), new InMemoryTelemetryStore())));

    [Fact]
    public Task EmptyRequestReturnsIllegalDataValue()
    {
        Assert.Equal(new byte[] { 0x83, 0x03 }, Create().Process([]));
        return Task.CompletedTask;
    }

    [Fact]
    public Task UnsupportedSingleByteFunctionReturnsIllegalFunction()
    {
        Assert.Equal(new byte[] { 0x86, 0x01 }, Create().Process([6]));
        return Task.CompletedTask;
    }

    [Fact]
    public Task TruncatedReadRequestReturnsIllegalDataValue()
    {
        Assert.Equal(new byte[] { 0x83, 0x03 }, Create().Process([3]));
        return Task.CompletedTask;
    }

    [Fact]
    public Task ExcessiveRegisterCountReturnsIllegalDataValue()
    {
        Assert.Equal(new byte[] { 0x83, 0x03 }, Create().Process([3, 0, 0, 0, 126]));
        return Task.CompletedTask;
    }

    [Fact]
    public Task ReadHoldingRegistersReturnsLevelAndPump()
    {
        var response = Create().Process([3, 0, 0, 0, 2]);
        Assert.Equal(new byte[] { 3, 4, 1, 244, 0, 0 }, response);
        return Task.CompletedTask;
    }

    [Fact]
    public Task WriteFunctionIsRejected()
    {
        var response = Create().Process([6, 0, 1, 0, 1]);
        Assert.Equal(new byte[] { 0x86, 0x01 }, response);
        return Task.CompletedTask;
    }

    [Fact]
    public Task UnknownAddressReturnsModbusException()
    {
        var response = Create().Process([3, 0, 99, 0, 1]);
        Assert.Equal(new byte[] { 0x83, 0x02 }, response);
        return Task.CompletedTask;
    }

    [Fact]
    public Task InvalidCountReturnsModbusException()
    {
        var response = Create().Process([3, 0, 0, 0, 0]);
        Assert.Equal(new byte[] { 0x83, 0x03 }, response);
        return Task.CompletedTask;
    }
}
