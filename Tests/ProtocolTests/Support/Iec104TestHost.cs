using System.Net;
using System.Net.Sockets;
using Application.Grid;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using IntegrationTests.Support;
using Protocols.Iec104;

namespace ProtocolTests.Support;

/// <summary>Hosts the API with the IEC 104 slave enabled on a free port and fake grid services.</summary>
public sealed class Iec104TestHost : IAsyncDisposable
{
    private Iec104TestHost(WebApplicationFactory<Program> factory, Iec104Server server, int port,
        Iec104FakeGridView view, Iec104FakeGridControl control)
    {
        Factory = factory;
        Server = server;
        Port = port;
        View = view;
        Control = control;
    }

    public WebApplicationFactory<Program> Factory { get; }
    public Iec104Server Server { get; }
    public int Port { get; }
    public Iec104FakeGridView View { get; }
    public Iec104FakeGridControl Control { get; }

    public static async Task<Iec104TestHost> StartAsync(params (string Key, string Value)[] settings)
    {
        var view = new Iec104FakeGridView();
        var control = new Iec104FakeGridControl();
        var port = FreePort();
        var factory = ApiTestFactory.Create([
            ("Simulation:Host:RunLoop", "false"),
            ("Protocols:Iec104:Enabled", "true"),
            ("Protocols:Iec104:Port", port.ToString()),
            ("Serilog:MinimumLevel:Default", "Error"),
            .. settings
        ]).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IGridView>(view);
            services.AddSingleton<IGridControl>(control);
        }));
        try
        {
            using var readiness = factory.CreateClient();
            var server = factory.Services.GetRequiredService<Iec104Server>();
            var bound = await server.Listening.WaitAsync(TimeSpan.FromSeconds(5));
            return new Iec104TestHost(factory, server, bound, view, control);
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
