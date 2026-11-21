using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Protocols.Modbus;

namespace IntegrationTests.Support;

public sealed class ModbusTestHost : IAsyncDisposable
{
    private ModbusTestHost(WebApplicationFactory<Program> factory, ModbusTcpServer server, int port)
    {
        Factory = factory;
        Server = server;
        Port = port;
    }

    public WebApplicationFactory<Program> Factory { get; }
    public ModbusTcpServer Server { get; }
    public int Port { get; }

    public static async Task<ModbusTestHost> StartAsync(params (string Key, string Value)[] settings)
    {
        var factory = ApiTestFactory.Create([
            ("Simulation:Host:RunLoop", "false"),
            ("Protocols:Modbus:Enabled", "true"),
            ("Protocols:Modbus:Port", FreePort().ToString()),
            ("Serilog:MinimumLevel:Default", "Error"),
            .. settings
        ]);
        try
        {
            using var readiness = factory.CreateClient();
            var server = factory.Services.GetRequiredService<ModbusTcpServer>();
            var port = await server.Listening.WaitAsync(TimeSpan.FromSeconds(5));
            return new ModbusTestHost(factory, server, port);
        }
        catch
        {
            await factory.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync() => Factory.DisposeAsync();

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
