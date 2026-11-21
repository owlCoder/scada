using System.Net;
using System.Net.Sockets;
using IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Protocols.Modbus;
using Xunit;

namespace IntegrationTests.Protocols;

public sealed class ModbusTcpTests
{
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory, int Port)> StartAsync()
    {
        var factory = ApiTestFactory.Create(("Protocols:Modbus:Enabled", "true"), ("Protocols:Modbus:Port", FreePort().ToString()));
        factory.CreateClient().Dispose();
        var port = await factory.Services.GetRequiredService<ModbusTcpServer>().Listening.WaitAsync(TimeSpan.FromSeconds(10));
        return (factory, port);
    }

    [Fact]
    public async Task ReadsHoldingRegistersOverTcp()
    {
        var (factory, port) = await StartAsync();
        using (factory)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();
            await stream.WriteAsync(new byte[] { 0, 7, 0, 0, 0, 6, 1, 0x03, 0, 100, 0, 2 });

            var response = new byte[13];
            await stream.ReadExactlyAsync(response);

            Assert.Equal(new byte[] { 0, 7, 0, 0, 0, 7, 1, 0x03, 4, 0x01, 0x90, 0x02, 0xEE }, response);
        }
    }

    [Fact]
    public async Task WritesAreRejectedByDefaultAndInvalidFramesCloseTheConnection()
    {
        var (factory, port) = await StartAsync();
        using (factory)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var stream = client.GetStream();
            await stream.WriteAsync(new byte[] { 0, 1, 0, 0, 0, 6, 1, 0x06, 0, 100, 0, 1 });
            var response = new byte[9];
            await stream.ReadExactlyAsync(response);
            Assert.Equal(new byte[] { 0x86, 0x01 }, response[7..]);

            await stream.WriteAsync(new byte[] { 0, 2, 0, 9, 0, 6, 1, 0x03, 0, 0, 0, 1 });
            var buffer = new byte[16];
            var read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, read);
        }
    }
}
