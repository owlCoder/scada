using System.Net;
using System.Net.Sockets;
using Application.Grid;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using IntegrationTests.Support;
using Protocols.Iec104;

namespace ProtocolTests.Support;

/// <summary>
/// Hosts the API with the IEC 104 slave enabled on a free port and the REAL grid services (engine, control, tagging):
/// unlike <see cref="Iec104TestHost"/> no <see cref="IGridControl"/> fake is registered, so wire commands reach
/// <c>GridControlService</c> and its tag enforcement.
/// </summary>
public sealed class DispatcherIec104TagHost : IAsyncDisposable
{
    private DispatcherIec104TagHost(WebApplicationFactory<Program> factory, Iec104Server server, int port)
    {
        Factory = factory;
        Server = server;
        Port = port;
    }

    public WebApplicationFactory<Program> Factory { get; }
    public Iec104Server Server { get; }
    public int Port { get; }

    public static async Task<DispatcherIec104TagHost> StartAsync(params (string Key, string Value)[] settings)
    {
        var port = FreePort();
        var factory = ApiTestFactory.Create([
            ("Simulation:Host:RunLoop", "false"),
            ("Grid:Enabled", "false"),
            ("Protocols:Iec104:Enabled", "true"),
            ("Protocols:Iec104:Port", port.ToString()),
            ("Serilog:MinimumLevel:Default", "Error"),
            .. settings
        ]);
        try
        {
            using var readiness = factory.CreateClient();
            var server = factory.Services.GetRequiredService<Iec104Server>();
            var bound = await server.Listening.WaitAsync(TimeSpan.FromSeconds(5));
            return new DispatcherIec104TagHost(factory, server, bound);
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
