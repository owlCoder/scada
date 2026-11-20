using Application;
using Domain;
using Infrastructure;
using Protocols;
using Xunit;

namespace UnitTests;

public sealed class ModbusFrameTests
{
    private static ModbusFrameService Create() =>
        new(new VirtualRegisterService(
            new PlantService(new Plant(), new InMemoryEventStore(), new InMemoryTelemetryStore())));

    [Fact]
    public void EmptyRequestReturnsIllegalDataValue()
    {
        Assert.Equal(new byte[] { 0x83, 0x03 }, Create().Process([]));
    }

    [Fact]
    public void UnsupportedSingleByteFunctionReturnsIllegalFunction()
    {
        Assert.Equal(new byte[] { 0x86, 0x01 }, Create().Process([6]));
    }

    [Fact]
    public void TruncatedReadRequestReturnsIllegalDataValue()
    {
        Assert.Equal(new byte[] { 0x83, 0x03 }, Create().Process([3]));
    }

    [Fact]
    public void ExcessiveRegisterCountReturnsIllegalDataValue()
    {
        Assert.Equal(new byte[] { 0x83, 0x03 }, Create().Process([3, 0, 0, 0, 126]));
    }

    [Fact]
    public void ReadHoldingRegistersReturnsLevelAndPump()
    {
        var response = Create().Process([3, 0, 0, 0, 2]);
        Assert.Equal(new byte[] { 3, 4, 1, 244, 0, 0 }, response);
    }

    [Fact]
    public void WriteFunctionIsRejected()
    {
        var response = Create().Process([6, 0, 1, 0, 1]);
        Assert.Equal(new byte[] { 0x86, 0x01 }, response);
    }

    [Fact]
    public void UnknownAddressReturnsModbusException()
    {
        var response = Create().Process([3, 0, 99, 0, 1]);
        Assert.Equal(new byte[] { 0x83, 0x02 }, response);
    }

    [Fact]
    public void InvalidCountReturnsModbusException()
    {
        var response = Create().Process([3, 0, 0, 0, 0]);
        Assert.Equal(new byte[] { 0x83, 0x03 }, response);
    }
}
