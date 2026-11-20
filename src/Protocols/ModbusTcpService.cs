using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Protocols;

public sealed class ModbusTcpService(ModbusFrameService frames, ILogger<ModbusTcpService> logger, int port)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        logger.LogInformation("Виртуелни Modbus TCP (само читање) слуша на 127.0.0.1:{Port}", port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClientAsync(client, stoppingToken);
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

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                var header = new byte[7];

                while (!cancellationToken.IsCancellationRequested)
                {
                    await stream.ReadExactlyAsync(header, cancellationToken);
                    var protocol = (header[2] << 8) | header[3];
                    var length = (header[4] << 8) | header[5];

                    if (protocol != 0 || length is < 2 or > 254)
                        return;

                    var request = new byte[length - 1];
                    await stream.ReadExactlyAsync(request, cancellationToken);
                    var response = frames.Process(request);
                    var reply = new byte[7 + response.Length];
                    reply[0] = header[0];
                    reply[1] = header[1];
                    reply[4] = (byte)((response.Length + 1) >> 8);
                    reply[5] = (byte)(response.Length + 1);
                    reply[6] = header[6];
                    response.CopyTo(reply, 7);
                    await stream.WriteAsync(reply, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
            {
                logger.LogDebug("Modbus клијент је прекинуо везу: {Message}", exception.Message);
            }
        }
    }
}
