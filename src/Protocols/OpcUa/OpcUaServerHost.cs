using System.Threading.Channels;
using Application.Commands;
using Application.Simulation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;

namespace Protocols.OpcUa;

public sealed class OpcUaServerHost(
    OpcUaOptions options, IOpcUaIdentityValidator identities, SimulationEngine simulation,
    ControlCommandService commands, TimeProvider time, ILoggerFactory loggers, ILogger<OpcUaServerHost> logger)
    : IHostedService, ISimulationObserver, IAsyncDisposable
{
    private const string ApplicationUri = "urn:scada-training:opcua-server";
    private readonly Channel<SimulationStepReport> _updates = Channel.CreateBounded<SimulationStepReport>(
        new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Lock _shutdown = new();
    private PlantOpcUaServer? _server;
    private Task _updateTask = Task.CompletedTask;
    private Task? _stopTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var telemetry = new OpcUaTelemetry(loggers);
        var application = new ApplicationInstance(telemetry)
        {
            ApplicationName = options.ApplicationName,
            ApplicationType = ApplicationType.Server
        };
        const string subject = "CN=SCADA Training Plant OPC UA";
        var address = $"opc.tcp://localhost:{options.Port}";
        // A DNS name in BaseAddresses makes the SDK listen on all interfaces.
        var builder = application.Build(ApplicationUri, ApplicationUri)
            .AsServer([$"opc.tcp://127.0.0.1:{options.Port}"])
            .AddUnsecurePolicyNone(options.AllowAnonymous)
            .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256)
            .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256)
            .AddUserTokenPolicy(UserTokenType.UserName);
        if (options.AllowAnonymous)
            builder.AddUserTokenPolicy(UserTokenType.Anonymous);

        var certificate = new CertificateIdentifier
        {
            SubjectName = subject,
            StoreType = CertificateStoreType.Directory,
            StorePath = Path.Combine(options.PkiDirectory, "own"),
            CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
        };
        var configuration = await builder
            .AddSecurityConfiguration([certificate], options.PkiDirectory)
            .SetAutoAcceptUntrustedCertificates(options.AutoAcceptUntrustedCertificates)
            .CreateAsync(cancellationToken);
        // The configuration builder resolves localhost to the machine name. Preserve the discovery alias.
        configuration.ServerConfiguration.AlternateBaseAddresses.Add(address);
        if (!OperatingSystem.IsWindows())
            Directory.CreateDirectory(Path.Combine(certificate.StorePath, "private"),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (!await application.CheckApplicationInstanceCertificatesAsync(silent: true, ct: cancellationToken))
            throw new InvalidOperationException("Could not load or create the OPC UA application certificate.");

        var server = new PlantOpcUaServer(options, identities, simulation, commands, time);
        try
        {
            await server.StartAsync(configuration, cancellationToken);
        }
        catch
        {
            server.Dispose();
            throw;
        }

        _server = server;
        _updateTask = UpdateNodesAsync(server);
        logger.LogInformation("OPC UA listening on {Address}", address);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_shutdown)
            return _stopTask ??= StopServerAsync(cancellationToken);
    }

    private async Task StopServerAsync(CancellationToken cancellationToken)
    {
        using var server = Interlocked.Exchange(ref _server, null);
        if (server is null)
            return;

        _updates.Writer.TryComplete();
        try
        {
            await _updateTask;
        }
        finally
        {
            await server.StopAsync(cancellationToken);
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    public ValueTask<int> OnStepAsync(SimulationStepReport report, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_updates.Writer.TryWrite(report) ? 1 : 0);

    private async Task UpdateNodesAsync(PlantOpcUaServer server)
    {
        await foreach (var report in _updates.Reader.ReadAllAsync())
            server.Nodes.Update(report.Plant.Snapshot, report.Controllers, report.Timestamp.UtcDateTime);
    }
}
