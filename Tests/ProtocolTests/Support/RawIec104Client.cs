using System.Net;
using System.Net.Sockets;
using Protocols.Iec104;
using Xunit;

namespace ProtocolTests.Support;

/// <summary>
/// Deliberately independent raw TCP client for IEC 60870-5-104 wire tests: it frames and parses APDUs itself but uses
/// only the public codec types of the server for building/parsing (no shared wire code with the connection loop).
/// Keeps one pending read so timeouts never abandon a frame.
/// </summary>
public sealed class RawIec104Client : IAsyncDisposable
{
    private NetworkStream? _stream;
    private Task<byte[]>? _pendingRead;
    private readonly Queue<byte[]> _buffer = new();

    public TcpClient Socket { get; } = new() { NoDelay = true };
    public NetworkStream Stream => _stream ??= Socket.GetStream();

    public static async Task<RawIec104Client> ConnectAsync(int port)
    {
        var client = new RawIec104Client();
        await client.Socket.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(3));
        return client;
    }

    public static byte[] Unnumbered(byte function) => new Apdu(Apci.Unnumbered(function), []).Serialize();

    public static byte[] Supervisory(int receiveSequence) => new Apdu(Apci.Supervisory(receiveSequence), []).Serialize();

    public static byte[] Information(int sendSequence, int receiveSequence, byte[] asdu) =>
        new Apdu(Apci.Information(sendSequence, receiveSequence), asdu).Serialize();

    public static byte[] AsduBytes(Iec104TypeId type, CauseOfTransmission cot, int commonAddress,
        params (int Ioa, byte[] Data)[] objects) =>
        Asdu.Build(new AsduHeader(type, false, objects.Length, cot, 0, commonAddress),
            objects.Select(o => new Iec104InformationObject(o.Ioa, o.Data)).ToArray()).Serialize();

    public async Task SendAsync(byte[] frame, CancellationToken cancellationToken = default) =>
        await Stream.WriteAsync(frame, cancellationToken);

    public async Task<byte[]> ReceiveFrameAsync(CancellationToken cancellationToken = default)
    {
        var header = new byte[Apdu.HeaderSize];
        await Stream.ReadExactlyAsync(header, cancellationToken);
        Assert.True(header[0] == Apdu.StartByte, $"expected start byte 0x68, got 0x{header[0]:X2}");
        var frame = new byte[Apdu.HeaderSize + header[1]];
        header.CopyTo(frame, 0);
        await Stream.ReadExactlyAsync(frame.AsMemory(Apdu.HeaderSize), cancellationToken);
        return frame;
    }

    /// <summary>Receives the next frame, reusing a pending read started by <see cref="TryReceiveAsync"/> or buffered by <see cref="IsClosedAsync"/>.</summary>
    public async Task<byte[]> ReceiveAsync()
    {
        if (_buffer.Count > 0)
            return _buffer.Dequeue();
        var read = _pendingRead ??= ReceiveFrameAsync();
        var frame = await read;
        _pendingRead = null;
        return frame;
    }

    /// <summary>Receives the next frame when one arrives within <paramref name="milliseconds"/>; null on timeout or close.</summary>
    public async Task<byte[]?> TryReceiveAsync(int milliseconds)
    {
        var read = _pendingRead ??= ReceiveFrameAsync();
        if (await Task.WhenAny(read, Task.Delay(milliseconds)) != read)
            return null;
        try
        {
            return await ReceiveAsync();
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or SocketException)
        {
            return null;
        }
    }

    public async Task<Apdu> ReceiveApduAsync()
    {
        var frame = await ReceiveAsync();
        var apdu = Apdu.TryParse(frame);
        Assert.True(apdu is not null, "received frame is not a valid APDU");
        return apdu.Value;
    }

    public async Task<(Asdu Asdu, Apci Control)> ReceiveAsduAsync()
    {
        var apdu = await ReceiveApduAsync();
        var asdu = Asdu.TryParse(apdu.Payload);
        Assert.True(asdu is not null, "received I-frame payload is not a valid ASDU");
        return (asdu, apdu.Control);
    }

    /// <summary>True when the server closed the connection: drains pending frames, then a read fails with EOF.</summary>
    public async Task<bool> IsClosedAsync()
    {
        while (true)
        {
            var read = _pendingRead ??= ReceiveFrameAsync();
            var completed = await Task.WhenAny(read, Task.Delay(2500));
            if (completed != read)
                return false;
            try
            {
                var frame = await read;
                _pendingRead = null;
                _buffer.Enqueue(frame);
            }
            catch (Exception exception) when (exception is EndOfStreamException or IOException or SocketException)
            {
                return true;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
