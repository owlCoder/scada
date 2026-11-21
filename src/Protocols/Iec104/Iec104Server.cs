using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Application.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Protocols.Iec104;

/// <summary>
/// IEC 60870-5-104 slave listener (controlled station) bound to an IPv4 address. Enforces <see cref="Iec104Options.MaxConnections"/>
/// and <see cref="Iec104Options.AllowedClients"/> (empty = loopback only), audits connection open/close/rejected and hands
/// every accepted client to an <see cref="Iec104Connection"/>.
/// </summary>
public sealed class Iec104Server(
    Iec104Options options,
    Iec104AsduHandler handler,
    IEventPublisher events,
    TimeProvider time,
    ILoggerFactory loggers,
    ILogger<Iec104Server> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<int, Iec104Connection> _connections = new();
    private readonly TaskCompletionSource<int> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IPAddress[] _allowedClients = ParseAllowedClients(options.AllowedClients);
    private int _nextConnectionId;
    private int _activeConnections;

    /// <summary>Completes with the bound port once the listener is accepting connections.</summary>
    public Task<int> Listening => _listening.Task;

    public int ActiveConnections => Volatile.Read(ref _activeConnections);

    public IReadOnlyCollection<Iec104Connection> Connections => _connections.Values.ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Parse(options.BindAddress), options.Port);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _listening.TrySetResult(port);
        logger.LogInformation("IEC 60870-5-104 listening on {Address}:{Port}", options.BindAddress, port);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
                if (!IsClientAllowed(remote))
                {
                    Audit($"IEC 104 connection from {client.Client.RemoteEndPoint} rejected: client is not in AllowedClients.",
                        EventSeverity.High, $"iec104:{client.Client.RemoteEndPoint}");
                    client.Dispose();
                    continue;
                }

                if (Interlocked.Increment(ref _activeConnections) > options.MaxConnections)
                {
                    Interlocked.Decrement(ref _activeConnections);
                    Audit($"IEC 104 connection from {client.Client.RemoteEndPoint} refused: connection limit {options.MaxConnections} reached.",
                        EventSeverity.Medium, $"iec104:{client.Client.RemoteEndPoint}");
                    client.Dispose();
                    continue;
                }

                var id = Interlocked.Increment(ref _nextConnectionId);
                var source = $"iec104:{client.Client.RemoteEndPoint}";
                Audit($"IEC 104 connection {id} from {client.Client.RemoteEndPoint} accepted.", EventSeverity.Information, source);
                var connection = new Iec104Connection(id, client, options, handler, events, time,
                    loggers.CreateLogger<Iec104Connection>(), OnConnectionClosed);
                _connections[id] = connection;
                _ = connection.RunAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (SocketException exception) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("IEC 104 listener stopped: {Message}", exception.Message);
        }
        finally
        {
            listener.Stop();
            foreach (var connection in _connections.Values)
                await connection.CloseAsync("server stopping", EventSeverity.Low);
            await Task.WhenAll(_connections.Values.Select(c => c.Completion.WaitAsync(stoppingToken)));
        }
    }

    /// <summary>Sends spontaneous (COT 3) frames to every started connection; returns how many were queued.</summary>
    public int Broadcast(IReadOnlyList<Asdu> frames)
    {
        if (frames.Count == 0)
            return 0;
        var sent = 0;
        foreach (var connection in _connections.Values)
        {
            if (!connection.Started)
                continue;
            foreach (var frame in frames)
                if (connection.TryEnqueueSpontaneous(frame))
                    sent++;
        }

        return sent;
    }

    private bool OnConnectionClosed(Iec104Connection connection)
    {
        var removed = _connections.TryRemove(connection.Id, out _);
        Interlocked.Decrement(ref _activeConnections);
        return removed;
    }

    private bool IsClientAllowed(IPAddress? remote)
    {
        if (remote is null)
            return false;
        if (_allowedClients.Length == 0)
            return IPAddress.IsLoopback(remote);
        return _allowedClients.Any(allowed => allowed.Equals(remote));
    }

    private bool Audit(string message, EventSeverity severity, string source) =>
        events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.Protocol, severity, source, message,
            options.CommandActor));

    private static IPAddress[] ParseAllowedClients(IReadOnlyList<string> clients) =>
        clients.Select(c => IPAddress.TryParse(c, out var address) ? address : null)
            .Where(a => a is not null)
            .Select(a => a!)
            .ToArray();
}
