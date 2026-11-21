using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Protocols.Modbus;

/// <summary>Modbus TCP listener with a configurable bind address, with a connection limit and per-request idle timeout.</summary>
public sealed class ModbusTcpServer(ModbusRequestProcessor processor, ModbusOptions options, ILogger<ModbusTcpServer> logger)
    : BackgroundService
{
    private readonly TaskCompletionSource<int> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _activeConnections;

    /// <summary>Completes with the bound port once the listener is accepting connections.</summary>
    public Task<int> Listening => _listening.Task;

    public int ActiveConnections => Volatile.Read(ref _activeConnections);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Parse(options.BindAddress), options.Port);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _listening.TrySetResult(port);
        logger.LogInformation("Modbus TCP listening on {Address}:{Port}", options.BindAddress, port);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                if (Interlocked.Increment(ref _activeConnections) > options.MaxConnections)
                {
                    Interlocked.Decrement(ref _activeConnections);
                    logger.LogWarning("Modbus connection from {Remote} refused: connection limit reached", client.Client.RemoteEndPoint);
                    client.Dispose();
                    continue;
                }

                _ = ServeAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task<bool> ServeAsync(TcpClient client, CancellationToken stoppingToken)
    {
        var source = $"modbus:{client.Client.RemoteEndPoint}";
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var header = new byte[MbapHeader.Size];
                while (!stoppingToken.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(options.IdleTimeout);
                    await stream.ReadExactlyAsync(header, timeout.Token);
                    var parsed = MbapHeader.TryParse(header);
                    var adu = new byte[MbapHeader.Size + (parsed?.PduLength ?? 0)];
                    header.CopyTo(adu, 0);
                    if (parsed is not null)
                        await stream.ReadExactlyAsync(adu.AsMemory(MbapHeader.Size), timeout.Token);

                    var response = processor.Process(adu, source);
                    if (response is null)
                        return false;
                    await stream.WriteAsync(response, timeout.Token);
                }

                return true;
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or EndOfStreamException)
        {
            logger.LogDebug("Modbus client {Source} disconnected: {Message}", source, exception.Message);
            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected Modbus error for {Source}", source);
            return false;
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
        }
    }
}
