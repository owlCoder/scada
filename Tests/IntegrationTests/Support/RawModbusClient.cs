using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace IntegrationTests.Support;

// Deliberately independent of the server's MBAP parser and any Modbus library.
public sealed class RawModbusClient : IAsyncDisposable
{
    private NetworkStream? _stream;

    public TcpClient Socket { get; } = new() { NoDelay = true };
    public NetworkStream Stream => _stream ??= Socket.GetStream();

    public static async Task<RawModbusClient> ConnectAsync(int port)
    {
        var client = new RawModbusClient();
        await client.Socket.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(3));
        return client;
    }

    public static byte[] Frame(byte unit, byte[] pdu, ushort transaction = 0x1234)
    {
        var frame = new byte[7 + pdu.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame, transaction);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), checked((ushort)(pdu.Length + 1)));
        frame[6] = unit;
        pdu.CopyTo(frame, 7);
        return frame;
    }

    public static byte[] Read(byte function = 3, ushort start = 100, ushort count = 2) =>
        [function, (byte)(start >> 8), (byte)start, (byte)(count >> 8), (byte)count];

    public async Task<byte[]> ExchangeAsync(byte unit, byte[] pdu, ushort transaction = 0x1234)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Stream.WriteAsync(Frame(unit, pdu, transaction), timeout.Token);
        var response = await ReceiveAsync(timeout.Token);
        Assert.Equal(transaction, BinaryPrimitives.ReadUInt16BigEndian(response));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2)));
        Assert.Equal(unit, response[6]);
        return response[7..];
    }

    public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
    {
        var header = new byte[7];
        await Stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
        Assert.InRange(length, 2, 254);
        var response = new byte[6 + length];
        header.CopyTo(response, 0);
        await Stream.ReadExactlyAsync(response.AsMemory(7), cancellationToken);
        return response;
    }

    public async Task<bool> IsClosedAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            return await Stream.ReadAsync(new byte[1], timeout.Token) == 0;
        }
        catch (IOException)
        {
            // TCP may reset rather than FIN when a malformed frame leaves unread bytes.
            return true;
        }
    }

    public ValueTask DisposeAsync()
    {
        Socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
