using Application.Events;
using Application.Registers;
using Protocols.Modbus;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Protocols.Modbus;

public sealed class ModbusRequestProcessorTests
{
    private const string Source = "modbus:127.0.0.1:50000";

    private static (ModbusRequestProcessor Processor, TestRig Rig) Create(bool writes = false)
    {
        var rig = new TestRig(new RegisterWriteOptions { WritesEnabled = writes });
        return (new ModbusRequestProcessor(rig.Registers, rig.RegisterOptions, new ModbusOptions { AllowWrites = writes }, rig.Monitor,
            rig.Events, rig.Time), rig);
    }

    private static byte[] Adu(byte unit, params byte[] pdu)
    {
        var adu = new byte[7 + pdu.Length];
        adu[0] = 0x12;
        adu[1] = 0x34;
        adu[5] = (byte)(pdu.Length + 1);
        adu[6] = unit;
        pdu.CopyTo(adu, 7);
        return adu;
    }

    private static byte[] Pdu(byte[] response) => response[7..];

    [Fact]
    public Task ReadHoldingRegistersReturnsSetpointsAndEchoesTheHeader()
    {
        var (processor, _) = Create();

        var response = processor.Process(Adu(1, 0x03, 0, 100, 0, 2), Source)!;

        Assert.Equal(new byte[] { 0x12, 0x34, 0, 0, 0, 7, 1 }, response[..7]);
        Assert.Equal(new byte[] { 0x03, 4, 0x01, 0x90, 0x02, 0xEE }, Pdu(response));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ReadInputRegistersReturnsMeasurements()
    {
        var (processor, rig) = Create();
        await rig.RunForAsync(2);

        var response = Pdu(processor.Process(Adu(1, 0x04, 0, 0, 0, 1), Source)!);

        Assert.Equal(0x04, response[0]);
        var level = (response[2] << 8 | response[3]) / 10.0;
        Assert.Equal(rig.Training.Snapshot().Sensor("LT-101")!.ReportedValue, level, 0);
    }

    [Theory]
    [InlineData(new byte[] { 0x03 }, new byte[] { 0x83, 0x03 })]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 0 }, new byte[] { 0x83, 0x03 })]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 126 }, new byte[] { 0x83, 0x03 })]
    [InlineData(new byte[] { 0x03, 0, 250, 0, 10 }, new byte[] { 0x83, 0x02 })]
    [InlineData(new byte[] { 0x06, 0, 100, 0, 1 }, new byte[] { 0x86, 0x01 })]
    [InlineData(new byte[] { 0x10, 0, 100, 0, 1, 2, 0, 1 }, new byte[] { 0x90, 0x01 })]
    [InlineData(new byte[] { 0x2B }, new byte[] { 0xAB, 0x01 })]
    public Task InvalidOrUnsupportedRequestsReturnModbusExceptions(byte[] pdu, byte[] expected)
    {
        var (processor, _) = Create();
        Assert.Equal(expected, Pdu(processor.Process(Adu(1, pdu), Source)!));
        return Task.CompletedTask;
    }

    [Fact]
    public Task UnknownUnitReturnsGatewayTargetFailure()
    {
        var (processor, rig) = Create();

        Assert.Equal(new byte[] { 0x83, 0x0B }, Pdu(processor.Process(Adu(9, 0x03, 0, 0, 0, 1), Source)!));
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Category == EventCategories.Protocol && e.Source == Source);
        return Task.CompletedTask;
    }

    [Fact]
    public Task MalformedMbapHeaderClosesTheConnection()
    {
        var (processor, _) = Create();
        var badProtocol = Adu(1, 0x03, 0, 0, 0, 1);
        badProtocol[3] = 1;
        var badLength = Adu(1, 0x03, 0, 0, 0, 1);
        badLength[5] = 20;

        Assert.Null(processor.Process(badProtocol, Source));
        Assert.Null(processor.Process(badLength, Source));
        Assert.Null(processor.Process([0, 1, 0], Source));
        return Task.CompletedTask;
    }

    [Fact]
    public Task EnabledSingleWriteEchoesTheRequestAndChangesTheSetpoint()
    {
        var (processor, rig) = Create(writes: true);

        var response = Pdu(processor.Process(Adu(1, 0x06, 0, 100, 0x01, 0xF4), Source)!);

        Assert.Equal(new byte[] { 0x06, 0, 100, 0x01, 0xF4 }, response);
        Assert.Equal((ushort)500, rig.Registers.Read(1, global::Domain.Control.RegisterArea.Holding, 100, 1, "test").Values[0]);
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Actor == "modbus-local" && e.Category == EventCategories.Audit);
        return Task.CompletedTask;
    }

    [Fact]
    public Task WritesNeedTheModbusOptInEvenWhenRegisterWritesAreEnabled()
    {
        var rig = new TestRig(new RegisterWriteOptions { WritesEnabled = true });
        var processor = new ModbusRequestProcessor(rig.Registers, rig.RegisterOptions, new ModbusOptions(), rig.Monitor, rig.Events, rig.Time);

        Assert.Equal(new byte[] { 0x86, 0x01 }, Pdu(processor.Process(Adu(1, 0x06, 0, 100, 0x01, 0xF4), Source)!));
        Assert.Equal((ushort)400, rig.Registers.Read(1, global::Domain.Control.RegisterArea.Holding, 100, 1, "test").Values[0]);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ModbusWritesAreRateLimitedPerClientHost()
    {
        var (processor, _) = Create(writes: true);
        for (var i = 0; i < 10; i++)
            Assert.Equal(0x06, Pdu(processor.Process(Adu(1, 0x06, 0, 100, 0x01, (byte)i), $"modbus:127.0.0.1:{50000 + i}")!)[0]);

        Assert.Equal(new byte[] { 0x86, 0x04 }, Pdu(processor.Process(Adu(1, 0x06, 0, 100, 0x01, 0xF4), "modbus:127.0.0.1:59999")!));
        return Task.CompletedTask;
    }

    [Fact]
    public Task MultipleWriteIsAllOrNothing()
    {
        var (processor, rig) = Create(writes: true);

        var rejected = Pdu(processor.Process(Adu(1, 0x10, 0, 199, 0, 2, 4, 0, 1, 0, 2), Source)!);
        var accepted = Pdu(processor.Process(Adu(1, 0x10, 0, 100, 0, 2, 4, 0x01, 0x2C, 0x02, 0x58), Source)!);

        Assert.Equal(new byte[] { 0x90, 0x02 }, rejected);
        Assert.Equal(new byte[] { 0x10, 0, 100, 0, 2 }, accepted);
        Assert.Equal(new ushort[] { 300, 600 }, rig.Registers.Read(1, global::Domain.Control.RegisterArea.Holding, 100, 2, "test").Values);
        var tooLarge = Pdu(processor.Process(Adu(1, 0x10, 0, 100, 0, 1, 2, 0x03, 0xE9), Source)!);
        Assert.Equal(new byte[] { 0x90, 0x03 }, tooLarge);
        return Task.CompletedTask;
    }
}
