using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using Application.Registers;
using Application.Simulation;
using Domain.Control;
using Microsoft.Extensions.DependencyInjection;
using IntegrationTests.Support;
using Xunit;

namespace ProtocolTests.Modbus;

public sealed class ModbusWireTests
{
    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    public async Task ReadsEntireRegisterImageAcrossUnits(int unit, int function)
    {
        await using var host = await ModbusTestHost.StartAsync();
        var services = host.Factory.Services;
        Assert.Equal(4, await services.GetRequiredService<SimulationCoordinator>().StepManyAsync(4, CancellationToken.None));
        var registers = services.GetRequiredService<RegisterAccessService>();
        await using var client = await RawModbusClient.ConnectAsync(host.Port);
        var area = function == 3 ? RegisterArea.Holding : RegisterArea.Input;
        for (ushort start = 0; start < 256; start += 125)
        {
            var count = (ushort)Math.Min(125, 256 - start);
            var expected = registers.Read((byte)unit, area, start, count, "test").Values;
            var pdu = await client.ExchangeAsync((byte)unit, RawModbusClient.Read((byte)function, start, count), start);
            Assert.Equal(function, pdu[0]);
            Assert.Equal(count * 2, pdu[1]);
            Assert.Equal(2 + count * 2, pdu.Length);
            Assert.Equal(expected, Enumerable.Range(0, count)
                .Select(i => BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(2 + i * 2))).ToArray());
        }
    }

    [Theory]
    [InlineData(0, 3, 100, 1, 0x0B)]
    [InlineData(4, 4, 100, 1, 0x0B)]
    [InlineData(255, 3, 100, 1, 0x0B)]
    [InlineData(1, 0x7F, 0, 1, 0x01)]
    [InlineData(1, 3, 256, 1, 0x02)]
    [InlineData(1, 4, 255, 2, 0x02)]
    [InlineData(1, 3, 65535, 2, 0x02)]
    [InlineData(1, 3, 0, 0, 0x03)]
    [InlineData(1, 4, 0, 126, 0x03)]
    [InlineData(1, 3, 0, 65535, 0x03)]
    public async Task ReturnsExactExceptionsAndKeepsConnectionUsable(int unit, int function, int start, int count, int code)
    {
        await using var host = await ModbusTestHost.StartAsync();
        await using var client = await RawModbusClient.ConnectAsync(host.Port);
        Assert.Equal(new byte[] { (byte)(function | 0x80), (byte)code },
            await client.ExchangeAsync((byte)unit, RawModbusClient.Read((byte)function, (ushort)start, (ushort)count)));
        Assert.Equal(new byte[] { 3, 4, 1, 144, 2, 238 }, await client.ExchangeAsync(1, RawModbusClient.Read()));
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 255)]
    [InlineData(0, 65535)]
    public async Task InvalidProtocolOrOversizedMbapClosesConnectionWithoutBody(int protocol, int length)
    {
        await using var host = await ModbusTestHost.StartAsync();
        await using var client = await RawModbusClient.ConnectAsync(host.Port);
        byte[] header = [0, 1, (byte)(protocol >> 8), (byte)protocol, (byte)(length >> 8), (byte)length, 1];
        await client.Stream.WriteAsync(header);
        Assert.True(await client.IsClosedAsync());
    }

    [Fact]
    public async Task DeclaredLengthIsAStreamBoundaryAndWrongPduLengthReturnsIllegalValue()
    {
        await using var host = await ModbusTestHost.StartAsync();
        await using var client = await RawModbusClient.ConnectAsync(host.Port);
        // Valid MBAP length, but an FC03 PDU with an extra byte is invalid, not a second request.
        Assert.Equal(new byte[] { 0x83, 3 }, await client.ExchangeAsync(1, [3, 0, 100, 0, 1, 0]));
        Assert.Equal(new byte[] { 0x83, 3 }, await client.ExchangeAsync(1, [3]));
        Assert.Equal(new byte[] { 3, 2, 1, 144 }, await client.ExchangeAsync(1, RawModbusClient.Read(count: 1)));
    }

    [Fact]
    public async Task FragmentedAndCoalescedFramesPreserveTransactions()
    {
        await using var host = await ModbusTestHost.StartAsync();
        await using var client = await RawModbusClient.ConnectAsync(host.Port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var frame = RawModbusClient.Frame(1, RawModbusClient.Read(), 17);
        foreach (var part in new[] { frame[..2], frame[2..7], frame[7..9], frame[9..] })
        {
            await client.Stream.WriteAsync(part, timeout.Token);
            await Task.Delay(10, timeout.Token);
        }
        var first = await client.ReceiveAsync(timeout.Token);
        Assert.Equal(17, BinaryPrimitives.ReadUInt16BigEndian(first));
        Assert.Equal(new byte[] { 3, 4, 1, 144, 2, 238 }, first[7..]);
        await client.Stream.WriteAsync(RawModbusClient.Frame(1, RawModbusClient.Read(), 18)
            .Concat(RawModbusClient.Frame(2, RawModbusClient.Read(), 19)).ToArray(), timeout.Token);
        foreach (var transaction in new[] { 18, 19 })
        {
            var response = await client.ReceiveAsync(timeout.Token);
            Assert.Equal(transaction, BinaryPrimitives.ReadUInt16BigEndian(response));
            Assert.Equal(transaction == 18 ? 1 : 2, response[6]);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task BothWriteOptInsAreRequiredForFc06AndFc16(bool registersEnabled, bool modbusEnabled)
    {
        await using var host = await ModbusTestHost.StartAsync(
            ("Registers:WritesEnabled", registersEnabled.ToString()), ("Protocols:Modbus:AllowWrites", modbusEnabled.ToString()));
        await using var client = await RawModbusClient.ConnectAsync(host.Port);
        byte[] single = [6, 0, 100, 2, 88];
        byte[] multiple = [16, 0, 100, 0, 2, 4, 1, 244, 3, 32];
        if (registersEnabled && modbusEnabled)
        {
            Assert.Equal(single, await client.ExchangeAsync(1, single));
            Assert.Equal(new byte[] { 3, 2, 2, 88 }, await client.ExchangeAsync(1, RawModbusClient.Read(count: 1)));
            Assert.Equal(multiple[..5], await client.ExchangeAsync(1, multiple));
            Assert.Equal(new byte[] { 3, 4, 1, 244, 3, 32 }, await client.ExchangeAsync(1, RawModbusClient.Read()));
        }
        else
        {
            Assert.Equal(new byte[] { 0x86, 1 }, await client.ExchangeAsync(1, single));
            Assert.Equal(new byte[] { 0x90, 1 }, await client.ExchangeAsync(1, multiple));
            Assert.Equal(new byte[] { 3, 4, 1, 144, 2, 238 }, await client.ExchangeAsync(1, RawModbusClient.Read()));
        }
    }

    [Theory]
    [InlineData(new byte[] { 6, 0, 99, 0, 1 }, 0x86, 2)]
    [InlineData(new byte[] { 6, 0, 100, 255, 255 }, 0x86, 3)]
    [InlineData(new byte[] { 16, 0, 199, 0, 2, 4, 0, 1, 0, 2 }, 0x90, 2)]
    [InlineData(new byte[] { 16, 0, 100, 0, 2, 4, 0, 1, 255, 255 }, 0x90, 3)]
    [InlineData(new byte[] { 16, 0, 100, 0, 2, 2, 0, 1 }, 0x90, 3)]
    [InlineData(new byte[] { 16, 0, 100, 0, 0, 0 }, 0x90, 3)]
    [InlineData(new byte[] { 16, 0, 100, 0, 124, 0 }, 0x90, 3)]
    public async Task InvalidWritesLeaveEntireImageUnchanged(byte[] request, byte function, byte code)
    {
        await using var host = await ModbusTestHost.StartAsync(("Registers:WritesEnabled", "true"), ("Protocols:Modbus:AllowWrites", "true"));
        await using var client = await RawModbusClient.ConnectAsync(host.Port);
        var before = await client.ExchangeAsync(1, RawModbusClient.Read(start: 99, count: 102));
        Assert.Equal(new[] { function, code }, await client.ExchangeAsync(1, request));
        Assert.Equal(before, await client.ExchangeAsync(1, RawModbusClient.Read(start: 99, count: 102)));
    }

    [Fact]
    public async Task ManyClientsHaveIndependentFramesAndResponses()
    {
        await using var host = await ModbusTestHost.StartAsync(("Protocols:Modbus:MaxConnections", "64"));
        await Task.WhenAll(Enumerable.Range(1, 32).Select(async index =>
        {
            await using var client = await RawModbusClient.ConnectAsync(host.Port);
            for (ushort request = 0; request < 20; request++)
                Assert.Equal(new byte[] { 3, 4, 1, 144, 2, 238 },
                    await client.ExchangeAsync(1, RawModbusClient.Read(), (ushort)(index * 100 + request)));
        })).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(9)]
    public async Task IdleAndIncompleteFramesTimeOutAndReleaseCapacity(int bytes)
    {
        await using var host = await ModbusTestHost.StartAsync(("Protocols:Modbus:IdleTimeout", "00:00:00.250"));
        await using var client = await RawModbusClient.ConnectAsync(host.Port);
        if (bytes > 0)
            await client.Stream.WriteAsync(RawModbusClient.Frame(1, RawModbusClient.Read()).AsMemory(0, bytes));
        Assert.True(await client.IsClosedAsync());
        Assert.True(await WaitForConnectionsAsync(host, 0));
        await using var next = await RawModbusClient.ConnectAsync(host.Port);
        Assert.Equal(3, (await next.ExchangeAsync(1, RawModbusClient.Read()))[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HalfCloseHandlesCompleteRequestAndDiscardsTruncatedRequest(bool complete)
    {
        await using var host = await ModbusTestHost.StartAsync();
        await using var client = await RawModbusClient.ConnectAsync(host.Port);
        var frame = RawModbusClient.Frame(1, RawModbusClient.Read());
        await client.Stream.WriteAsync(complete ? frame : frame[..9]);
        client.Socket.Client.Shutdown(SocketShutdown.Send);
        if (complete)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            Assert.Equal(new byte[] { 3, 4, 1, 144, 2, 238 }, (await client.ReceiveAsync(timeout.Token))[7..]);
        }
        Assert.True(await client.IsClosedAsync());
        Assert.True(await WaitForConnectionsAsync(host, 0));
    }

    [Fact]
    public async Task ConnectionLimitRejectsExcessAndRecoversAfterDisconnect()
    {
        await using var host = await ModbusTestHost.StartAsync(("Protocols:Modbus:MaxConnections", "2"));
        await using var first = await RawModbusClient.ConnectAsync(host.Port);
        await using var second = await RawModbusClient.ConnectAsync(host.Port);
        Assert.True(await WaitForConnectionsAsync(host, 2));
        await using var rejected = await RawModbusClient.ConnectAsync(host.Port);
        Assert.True(await rejected.IsClosedAsync());
        Assert.Equal(2, host.Server.ActiveConnections);
        Assert.Equal(3, (await second.ExchangeAsync(1, RawModbusClient.Read()))[0]);
        await first.DisposeAsync();
        Assert.True(await WaitForConnectionsAsync(host, 1));
        await using var replacement = await RawModbusClient.ConnectAsync(host.Port);
        Assert.Equal(3, (await replacement.ExchangeAsync(1, RawModbusClient.Read()))[0]);
    }

    private static async Task<bool> WaitForConnectionsAsync(ModbusTestHost host, int expected)
    {
        var watch = Stopwatch.StartNew();
        while (host.Server.ActiveConnections != expected && watch.Elapsed < TimeSpan.FromSeconds(2))
            await Task.Delay(10);
        return host.Server.ActiveConnections == expected;
    }
}
