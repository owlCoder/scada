using System.Net.Sockets;
using System.Threading.Channels;
using Application.Events;
using Microsoft.Extensions.Logging;

namespace Protocols.Iec104;

/// <summary>
/// One IEC 60870-5-104 master connection: APDU framing, I/S/U frame handling, 15-bit sequence numbers, k/w flow
/// control, t0-t3 timers and STARTDT gating. All state changes happen on the read loop thread except spontaneous
/// sends, which take <see cref="_sync"/>. Every connection owns a bounded outbound queue, so a slow client is closed
/// instead of blocking the simulation or other clients. Nothing here throws out of <see cref="RunAsync"/>.
/// </summary>
public sealed class Iec104Connection
{
    private readonly Lock _sync = new();
    private readonly Iec104Options _options;
    private readonly Iec104AsduHandler _handler;
    private readonly IEventPublisher _events;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Func<Iec104Connection, bool> _onClosed;
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Channel<byte[]> _outbound;
    private readonly CancellationTokenSource _internal = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DateTimeOffset _acceptedAt;
    private readonly int _pendingLimit;
    private int _closed;
    private int _sendSequence;      // V(S): next send sequence number
    private int _receiveSequence;   // V(R): next expected receive sequence number
    private int _unackedSent;
    private int _unackedReceived;
    private bool _started;
    private bool _everStarted;
    private bool _testPending;
    private DateTimeOffset _lastActivity;
    private DateTimeOffset _lastSentAt;
    private DateTimeOffset _lastReceivedAt;
    private readonly Queue<Asdu> _pendingInformation = new();
    private Task _writerTask = Task.CompletedTask;

    public Iec104Connection(int id, TcpClient client, Iec104Options options, Iec104AsduHandler handler,
        IEventPublisher events, TimeProvider time, ILogger logger, Func<Iec104Connection, bool> onClosed)
    {
        Id = id;
        _client = client;
        _options = options;
        _handler = handler;
        _events = events;
        _time = time;
        _logger = logger;
        _onClosed = onClosed;
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        Source = $"iec104:{remote}";
        // Select-before-operate ownership in the grid is by actor name, so every master connection gets its own
        // command actor: {CommandActor}@{remote ip}:{port}#{connection id}. The configured role is kept.
        CommandActorName = $"{options.CommandActor}@{remote}#{id}";
        _stream = client.GetStream();
        _acceptedAt = time.GetUtcNow();
        _lastActivity = _acceptedAt;
        _lastSentAt = _acceptedAt;
        _lastReceivedAt = _acceptedAt;
        _pendingLimit = Math.Max(64, options.K * 4);
        _outbound = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(Math.Max(16, options.K * 2 + 8))
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
    }

    public int Id { get; }
    public string Source { get; }

    /// <summary>Per-connection command actor name: <c>{CommandActor}@{remote ip}:{port}#{connection id}</c>.</summary>
    public string CommandActorName { get; }

    /// <summary>Set when <see cref="RunAsync"/> has finished, successfully or not.</summary>
    public Task Completion => _completion.Task;

    public bool Started
    {
        get { lock (_sync) return _started; }
    }

    public string? CloseReason { get; private set; }

    /// <summary>Runs the connection loop until the peer disconnects, a timer fires or the server stops.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            _writerTask = WriterLoopAsync(_outbound.Reader, cancellationToken);
            var readTask = ReadFrameAsync(cancellationToken);
            while (Volatile.Read(ref _closed) == 0 && !cancellationToken.IsCancellationRequested)
            {
                var now = _time.GetUtcNow();
                DateTimeOffset deadline;
                lock (_sync)
                    deadline = NextDeadlineLocked(now);
                var delay = Task.Delay(Math.Max(1, (int)(deadline - now).TotalMilliseconds) + 15, _internal.Token);
                var completed = await Task.WhenAny(readTask, delay);
                if (completed == delay)
                {
                    if (await HandleTimersAsync())
                        break;
                    continue;
                }

                byte[]? frame;
                try
                {
                    frame = await readTask;
                }
                catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException
                    or EndOfStreamException or InvalidDataException)
                {
                    await CloseAsync(exception is InvalidDataException
                        ? $"malformed APDU ({exception.Message})"
                        : "connection closed by peer", EventSeverity.Low);
                    break;
                }

                if (frame is null)
                {
                    await CloseAsync("peer closed the connection", EventSeverity.Low);
                    break;
                }

                readTask = ReadFrameAsync(cancellationToken);
                await ProcessFrameAsync(frame, cancellationToken);
                if (Volatile.Read(ref _closed) == 1)
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "IEC 104 connection {Source} failed unexpectedly", Source);
            await CloseAsync($"internal error ({exception.Message})", EventSeverity.Medium);
        }
        finally
        {
            await CloseAsync("connection closed", EventSeverity.Low);
            _outbound.Writer.TryComplete();
            try
            {
                await _writerTask.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
            }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or IOException)
            {
                // The writer is best effort; the socket is disposed below anyway.
            }

            _completion.TrySetResult();
        }
    }

    /// <summary>Queues a spontaneous (COT 3) ASDU as an I-frame. Called from the grid observer thread.</summary>
    public bool TryEnqueueSpontaneous(Asdu asdu)
    {
        bool writeFailed;
        lock (_sync)
        {
            if (Volatile.Read(ref _closed) == 1 || !_started)
                return false;
            if (_unackedSent >= _options.K)
            {
                if (_pendingInformation.Count >= _pendingLimit)
                {
                    Audit($"Dropping spontaneous frame for slow client {Source}: pending queue full.", EventSeverity.Medium);
                    return false;
                }

                _pendingInformation.Enqueue(asdu);
                return true;
            }

            writeFailed = !TryWriteInformationLocked(asdu);
        }

        if (writeFailed)
            _ = CloseAsync("slow client: outbound queue full", EventSeverity.Medium);
        return !writeFailed;
    }

    /// <summary>Closes the connection at once. Idempotent; safe to call from any thread.</summary>
    public Task CloseAsync(string reason, EventSeverity severity)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return Task.CompletedTask;
        CloseReason = reason;
        _outbound.Writer.TryComplete();
        _internal.Cancel();
        Audit($"IEC 104 connection {Source} closed: {reason}.", severity);
        _onClosed(this);
        _client.Dispose();
        return Task.CompletedTask;
    }

    private async Task<bool> ProcessFrameAsync(byte[] frame, CancellationToken cancellationToken)
    {
        var apdu = Apdu.TryParse(frame);
        if (apdu is null)
        {
            Audit("Malformed APDU (bad start byte, length or control field); closing connection.", EventSeverity.Medium);
            await CloseAsync("malformed APDU", EventSeverity.Medium);
            return false;
        }

        var control = apdu.Value.Control;
        lock (_sync)
        {
            _lastActivity = _time.GetUtcNow();
            _lastReceivedAt = _lastActivity;
            _testPending = false;
        }

        switch (control.Kind)
        {
            case ApduFrameKind.Unnumbered:
                return ProcessUnnumbered(control);
            case ApduFrameKind.Supervisory:
                return ProcessAcknowledgement(control.ReceiveSequence);
            case ApduFrameKind.Information:
                return await ProcessInformationAsync(apdu.Value, control, cancellationToken);
            default:
                Audit($"Unknown frame kind {control.Kind}; closing connection.", EventSeverity.Medium);
                await CloseAsync("unknown frame kind", EventSeverity.Medium);
                return false;
        }
    }

    private bool ProcessUnnumbered(Apci control)
    {
        switch (control.UnnumberedFunction)
        {
            case Apci.StartDtAct:
                lock (_sync)
                {
                    _started = true;
                    _everStarted = true;
                }

                Audit($"STARTDT act received from {Source}; data transfer started.", EventSeverity.Information);
                return SendUnnumbered(Apci.StartDtCon);
            case Apci.StopDtAct:
                lock (_sync)
                    _started = false;
                Audit($"STOPDT act received from {Source}; data transfer stopped.", EventSeverity.Information);
                return SendUnnumbered(Apci.StopDtCon);
            case Apci.TestFrAct:
                return SendUnnumbered(Apci.TestFrCon);
            case Apci.TestFrCon:
                return true;
            default:
                Audit($"Unsupported U-frame function 0x{control.UnnumberedFunction:X2} ignored.", EventSeverity.Low);
                return true;
        }
    }

    private async Task<bool> ProcessInformationAsync(Apdu apdu, Apci control, CancellationToken cancellationToken)
    {
        if (!Started)
        {
            Audit("I-frame received before STARTDT; closing connection.", EventSeverity.Medium);
            await CloseAsync("data before STARTDT", EventSeverity.Medium);
            return false;
        }

        if (!ProcessAcknowledgement(control.ReceiveSequence))
            return false;

        int expected;
        lock (_sync)
            expected = _receiveSequence;
        if (control.SendSequence != expected)
        {
            Audit($"Unexpected send sequence {control.SendSequence} (expected {expected}); re-acknowledging.", EventSeverity.Medium);
            return SendSupervisory(expected);
        }

        var asdu = Asdu.TryParse(apdu.Payload);
        if (asdu is null)
        {
            Audit("Malformed ASDU (header or payload length mismatch); closing connection.", EventSeverity.Medium);
            await CloseAsync("malformed ASDU", EventSeverity.Medium);
            return false;
        }

        IReadOnlyList<Asdu> responses;
        try
        {
            responses = await _handler.HandleAsync(asdu, Source, CommandActorName, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "IEC 104 ASDU handling failed for {Source}", Source);
            Audit($"ASDU handling failed ({exception.Message}); closing connection.", EventSeverity.Medium);
            await CloseAsync("ASDU handling error", EventSeverity.Medium);
            return false;
        }

        // The received frame is consumed before the responses are sent, so responses carry the updated V(R).
        lock (_sync)
        {
            _receiveSequence = Apci.Next(_receiveSequence);
            _unackedReceived++;
        }

        var writeFailed = false;
        foreach (var response in responses)
        {
            lock (_sync)
                writeFailed |= !SendInformationLocked(response);
        }

        lock (_sync)
        {
            if (_unackedReceived >= _options.W)
            {
                _unackedReceived = 0;
                writeFailed |= !TryWriteSupervisoryLocked(_receiveSequence);
            }
        }

        if (writeFailed)
        {
            await CloseAsync("slow client: outbound queue full", EventSeverity.Medium);
            return false;
        }

        return true;
    }

    /// <summary>Applies the master's receive sequence number (acknowledgement of our I-frames) and flushes pending frames.</summary>
    private bool ProcessAcknowledgement(int acknowledgement)
    {
        bool invalid;
        bool writeFailed = false;
        lock (_sync)
        {
            var unacked = Apci.Distance(acknowledgement, _sendSequence);
            invalid = unacked > _options.K;
            if (!invalid)
            {
                _unackedSent = unacked;
                while (_unackedSent < _options.K && _pendingInformation.Count > 0)
                    writeFailed |= !TryWriteInformationLocked(_pendingInformation.Dequeue());
            }
        }

        if (invalid)
        {
            Audit($"Invalid acknowledgement sequence {acknowledgement} (k={_options.K}); closing connection.", EventSeverity.Medium);
            _ = CloseAsync("invalid acknowledgement", EventSeverity.Medium);
            return false;
        }

        if (writeFailed)
        {
            _ = CloseAsync("slow client: outbound queue full", EventSeverity.Medium);
            return false;
        }

        return true;
    }

    private bool SendInformationLocked(Asdu asdu)
    {
        if (_unackedSent >= _options.K)
        {
            if (_pendingInformation.Count >= _pendingLimit)
            {
                Audit($"Pending I-frame queue full for {Source}; closing connection.", EventSeverity.Medium);
                return false;
            }

            _pendingInformation.Enqueue(asdu);
            return true;
        }

        return TryWriteInformationLocked(asdu);
    }

    private bool TryWriteInformationLocked(Asdu asdu)
    {
        var frame = new Apdu(Apci.Information(_sendSequence, _receiveSequence), asdu.Serialize()).Serialize();
        _sendSequence = Apci.Next(_sendSequence);
        _unackedSent++;
        _lastSentAt = _time.GetUtcNow();
        _lastActivity = _lastSentAt;
        return _outbound.Writer.TryWrite(frame);
    }

    private bool SendSupervisory(int receiveSequence)
    {
        lock (_sync)
            return TryWriteSupervisoryLocked(receiveSequence);
    }

    private bool TryWriteSupervisoryLocked(int receiveSequence)
    {
        _lastActivity = _time.GetUtcNow();
        return _outbound.Writer.TryWrite(new Apdu(Apci.Supervisory(receiveSequence), []).Serialize());
    }

    private bool SendUnnumbered(byte function)
    {
        lock (_sync)
        {
            _lastActivity = _time.GetUtcNow();
            if (function == Apci.TestFrAct)
            {
                _testPending = true;
                _lastSentAt = _lastActivity;
            }

            return _outbound.Writer.TryWrite(new Apdu(Apci.Unnumbered(function), []).Serialize());
        }
    }

    /// <summary>Returns true when the connection was closed by a timer.</summary>
    private async Task<bool> HandleTimersAsync()
    {
        var now = _time.GetUtcNow();
        string? closeReason = null;
        lock (_sync)
        {
            if (!_everStarted && now - _acceptedAt >= _options.T0Timeout)
                closeReason = $"t0 timeout: no STARTDT within {_options.T0}s";
            else if ((_unackedSent > 0 || _testPending) && now - _lastSentAt >= _options.T1Timeout)
                closeReason = $"t1 timeout: no acknowledgement within {_options.T1}s";
            else if (_unackedReceived > 0 && now - _lastReceivedAt >= _options.T2Timeout)
            {
                _unackedReceived = 0;
                TryWriteSupervisoryLocked(_receiveSequence);
            }
            else if (now - _lastActivity >= _options.T3Timeout)
            {
                Audit($"t3 idle timeout; sending TESTFR act to {Source}.", EventSeverity.Low);
                _testPending = true;
                _lastSentAt = now;
                _lastActivity = now;
                _outbound.Writer.TryWrite(new Apdu(Apci.Unnumbered(Apci.TestFrAct), []).Serialize());
            }
        }

        if (closeReason is not null)
        {
            Audit($"IEC 104 {closeReason}; closing connection {Source}.", EventSeverity.Medium);
            await CloseAsync(closeReason, EventSeverity.Medium);
            return true;
        }

        return false;
    }

    private DateTimeOffset NextDeadlineLocked(DateTimeOffset now)
    {
        var deadline = _lastActivity + _options.T3Timeout;
        if (!_everStarted)
            deadline = Min(deadline, _acceptedAt + _options.T0Timeout);
        if (_unackedSent > 0 || _testPending)
            deadline = Min(deadline, _lastSentAt + _options.T1Timeout);
        if (_unackedReceived > 0)
            deadline = Min(deadline, _lastReceivedAt + _options.T2Timeout);
        return deadline;
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;

    private async Task<byte[]?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _internal.Token);
        var header = new byte[Apdu.HeaderSize];
        await _stream.ReadExactlyAsync(header, linked.Token);
        if (header[0] != Apdu.StartByte)
            throw new InvalidDataException($"bad start byte 0x{header[0]:X2}");
        var length = header[1];
        if (length is < Apdu.MinLength or > Apdu.MaxLength)
            throw new InvalidDataException($"invalid APDU length {length}");
        var frame = new byte[Apdu.HeaderSize + length];
        header.CopyTo(frame, 0);
        await _stream.ReadExactlyAsync(frame.AsMemory(Apdu.HeaderSize), linked.Token);
        return frame;
    }

    private async Task WriterLoopAsync(ChannelReader<byte[]> reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in reader.ReadAllAsync(cancellationToken))
                await _stream.WriteAsync(frame, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
        {
            await CloseAsync("write failed", EventSeverity.Low);
        }
    }

    private bool Audit(string message, EventSeverity severity) =>
        _events.Publish(SecurityEvent.Create(_time.GetUtcNow(), EventCategories.Protocol, severity, Source, message,
            _options.CommandActor));
}
