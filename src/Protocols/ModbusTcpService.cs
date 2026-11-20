using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Protocols;

public sealed class ModbusTcpService(ModbusFrameService frames, ILogger<ModbusTcpService> logger, int port)
    : BackgroundService
{
    private const int MaxClients = 16;
    private static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        using var slots = new SemaphoreSlim(MaxClients);
        listener.Start();
        logger.LogInformation("Виртуелни Modbus TCP (само читање) слуша на 127.0.0.1:{Port}", port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                if (!await slots.WaitAsync(0, stoppingToken))
                {
                    client.Dispose();
                    continue;
                }

                _ = ServeAsync(client, slots, stoppingToken);
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

    private async Task ServeAsync(TcpClient client, SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        try
        {
            await HandleClientAsync(client, stoppingToken);
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken stoppingToken)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                var header = new byte[7];

                while (!stoppingToken.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(ClientTimeout);
                    await stream.ReadExactlyAsync(header, timeout.Token);

                    var protocol = (header[2] << 8) | header[3];
                    var length = (header[4] << 8) | header[5];
                    if (protocol != 0 || length is < 2 or > 254)
                        return;

                    var request = new byte[length - 1];
                    await stream.ReadExactlyAsync(request, timeout.Token);
                    var response = frames.Process(request);
                    var reply = new byte[7 + response.Length];
                    reply[0] = header[0];
                    reply[1] = header[1];
                    reply[4] = (byte)((response.Length + 1) >> 8);
                    reply[5] = (byte)(response.Length + 1);
                    reply[6] = header[6];
                    response.CopyTo(reply, 7);
                    await stream.WriteAsync(reply, timeout.Token);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
            {
                logger.LogDebug("Modbus клијент је прекинуо везу: {Message}", exception.Message);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Неочекивана грешка Modbus клијента");
            }
        }
    }
}
