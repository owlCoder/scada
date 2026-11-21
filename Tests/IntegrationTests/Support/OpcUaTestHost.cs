using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Xunit;

namespace IntegrationTests.Support;

internal sealed class OpcUaTestHost : IAsyncDisposable
{
    public const string NamespaceUri = "urn:scada-training:plant";
    public const string OperatorPassword = "opc-test-operator-password";
    public const string ViewerPassword = "opc-test-viewer-password";
    private readonly DirectoryInfo _pki = Directory.CreateTempSubdirectory("scada-opcua-");
    private readonly ITelemetryContext _telemetry = DefaultTelemetry.Create(_ => { });
    private readonly int _port = FreePort();
    private ApplicationConfiguration? _clientConfiguration;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string ServerPki => Path.Combine(_pki.FullName, "server");
    public int Port => _port;
    public Uri Address => new($"opc.tcp://localhost:{_port}");

    public static async Task<OpcUaTestHost> StartAsync(params (string Key, string Value)[] settings)
    {
        var host = new OpcUaTestHost();
        try
        {
            host.Factory = ApiTestFactory.Create([
                ("Protocols:OpcUa:Enabled", "true"),
                ("Protocols:OpcUa:Port", host.Port.ToString()),
                ("Protocols:OpcUa:PkiDirectory", host.ServerPki),
                ("Simulation:Host:RunLoop", "false"),
                ("Security:Users:0:Password", OperatorPassword),
                ("Security:Users:1:Username", "opc-viewer"),
                ("Security:Users:1:Role", "Viewer"),
                ("Security:Users:1:Password", ViewerPassword),
                .. settings
            ]);
            using var readiness = host.Factory.CreateClient();
            host._clientConfiguration = await host.ConfigureClientAsync();
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    public async Task<EndpointDescriptionCollection> DiscoverAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = await DiscoveryClient.CreateAsync(_clientConfiguration, Address, ct: timeout.Token);
        var response = await client.GetEndpointsAsync(null, Address.ToString(), null, null, timeout.Token);
        return response.Endpoints;
    }

    public async Task<ISession> ConnectAsync(MessageSecurityMode mode = MessageSecurityMode.None,
        string? username = null, string? password = null)
    {
        var endpoints = await DiscoverAsync();
        var endpoint = Assert.Single(endpoints, endpoint => endpoint.SecurityMode == mode);
        return await ConnectAsync(endpoint, username, password);
    }

    public async Task<ISession> ConnectAsync(EndpointDescription endpoint, string? username = null, string? password = null)
    {
        var configuration = _clientConfiguration!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var identity = username is null
            ? new UserIdentity()
            : new UserIdentity(username, Encoding.UTF8.GetBytes(password!));
        var factory = new DefaultSessionFactory(_telemetry);
        return await factory.CreateAsync(configuration,
            new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(configuration)),
            updateBeforeConnect: false, checkDomain: true, "SCADA integration tests", 30_000,
            identity, preferredLocales: null, ct: timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();
        _pki.Delete(recursive: true);
    }

    public static NodeId PlantNode(ISession session, string path) =>
        new(path, checked((ushort)session.NamespaceUris.GetIndex(NamespaceUri)));

    public static async Task<ReferenceDescriptionCollection> BrowseAsync(ISession session, NodeId nodeId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var browser = new Browser(session) { BrowseDirection = BrowseDirection.Forward, ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences };
        return await browser.BrowseAsync(nodeId, timeout.Token);
    }

    public static async Task<double> ReadDoubleAsync(ISession session, string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return Assert.IsType<double>((await session.ReadValueAsync(PlantNode(session, path), timeout.Token)).Value);
    }

    public static async Task<StatusCode> CallAsync(ISession session, string devicePath, string method, params Variant[] arguments)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await session.CallAsync(null,
            [new CallMethodRequest
            {
                ObjectId = PlantNode(session, devicePath),
                MethodId = PlantNode(session, $"{devicePath}/{method}"),
                InputArguments = new VariantCollection(arguments)
            }], timeout.Token);
        return Assert.Single(result.Results).StatusCode;
    }

    private async Task<ApplicationConfiguration> ConfigureClientAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var client = new ApplicationInstance(_telemetry)
        {
            ApplicationName = "SCADA OPC UA test client",
            ApplicationType = ApplicationType.Client
        };
        var root = Path.Combine(_pki.FullName, "client");
        var certificate = new CertificateIdentifier
        {
            SubjectName = "CN=SCADA OPC UA test client",
            StoreType = CertificateStoreType.Directory,
            StorePath = Path.Combine(root, "own"),
            CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
        };
        var configuration = await client.Build("urn:scada-training:integration-client", "urn:scada-training:integration-client")
            .AsClient()
            .AddSecurityConfiguration([certificate], root)
            .SetAutoAcceptUntrustedCertificates(true)
            .CreateAsync(timeout.Token);
        Assert.True(await client.CheckApplicationInstanceCertificatesAsync(silent: true, ct: timeout.Token));
        return configuration;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
