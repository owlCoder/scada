using Application;
using Domain;
using Infrastructure;
using Protocols;
using Xunit;

namespace UnitTests.Protocols;

public sealed class VirtualRegisterTests
{
    private static VirtualRegisterService Create() =>
        new(new PlantService(new Plant(), new InMemoryEventStore(), new InMemoryTelemetryStore()));

    [Fact]
    public Task RegistersExposePlantState()
    {
        var registers = Create().ReadAll();
        Assert.Equal(5, registers.Count);
        Assert.Equal((ushort)500, registers[0].Value);
        Assert.Equal((ushort)1, registers[2].Value);
        return Task.CompletedTask;
    }

    [Fact]
    public Task WritableRegistersChangePlant()
    {
        var registers = Create();
        Assert.True(registers.Write(1, 1, "operator"));
        Assert.Equal((ushort)1, registers.Read(1)!.Value);
        Assert.True(registers.Write(2, 0, "operator"));
        Assert.Equal((ushort)0, registers.Read(2)!.Value);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ReadOnlyAndInvalidValuesAreRejected()
    {
        var registers = Create();
        Assert.False(registers.Write(0, 1, "operator"));
        Assert.False(registers.Write(1, 2, "operator"));
        Assert.Null(registers.Read(100));
        return Task.CompletedTask;
    }
}
