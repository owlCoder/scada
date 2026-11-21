using System.Net;
using System.Net.Sockets;
using Application.Identity;

namespace Protocols.Iec104;

/// <summary>
/// IEC 60870-5-104 slave (controlled station) settings under <c>Protocols:Iec104</c>. The server exposes the grid RTUs
/// of <see cref="Application.Grid.IGridView"/> the way a real SCADA master talks to substations.
/// </summary>
public sealed class Iec104Options
{
    public bool Enabled { get; set; }
    public string BindAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 2404;
    public int MaxConnections { get; set; } = 4;

    /// <summary>k: maximum unacknowledged I-frames the slave sends before it must wait for an S-frame.</summary>
    public int K { get; set; } = 12;

    /// <summary>w: number of received I-frames after which the slave acknowledges with an S-frame.</summary>
    public int W { get; set; } = 8;

    /// <summary>t0 in seconds: connection establishment timeout (STARTDT must arrive within t0).</summary>
    public int T0 { get; set; } = 30;

    /// <summary>t1 in seconds: acknowledgement timeout for sent I-frames and TESTFR; expiry closes the connection.</summary>
    public int T1 { get; set; } = 15;

    /// <summary>t2 in seconds: maximum delay before acknowledging received I-frames with an S-frame.</summary>
    public int T2 { get; set; } = 10;

    /// <summary>t3 in seconds: idle supervision; on expiry the slave sends TESTFR act.</summary>
    public int T3 { get; set; } = 20;

    /// <summary>Allowed client IPv4 addresses. Empty means loopback clients only.</summary>
    public List<string> AllowedClients { get; set; } = [];

    /// <summary>When false, command ASDUs are answered with a negative activation confirmation.</summary>
    public bool CommandsEnabled { get; set; } = true;

    /// <summary>Actor name attributed to every command; the protocol cannot authenticate the client.</summary>
    public string CommandActor { get; set; } = "iec104-master";

    /// <summary>Role attributed to commands; must be at least Operator.</summary>
    public AccessRole CommandRole { get; set; } = AccessRole.Operator;

    public TimeSpan T0Timeout => TimeSpan.FromSeconds(T0);
    public TimeSpan T1Timeout => TimeSpan.FromSeconds(T1);
    public TimeSpan T2Timeout => TimeSpan.FromSeconds(T2);
    public TimeSpan T3Timeout => TimeSpan.FromSeconds(T3);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!IPAddress.TryParse(BindAddress, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            errors.Add("Iec104 BindAddress must be an IPv4 address.");
        if (Port is < 1024 or > 65535)
            errors.Add("Iec104 port must be between 1024 and 65535.");
        if (MaxConnections is < 1 or > 64)
            errors.Add("Iec104 MaxConnections must be between 1 and 64.");
        if (K is < 1 or > 32767)
            errors.Add("Iec104 K must be between 1 and 32767.");
        if (W is < 1 or > 32767)
            errors.Add("Iec104 W must be between 1 and 32767.");
        if (W > K)
            errors.Add("Iec104 W must not exceed K.");
        foreach (var (name, value) in new[] { ("T0", T0), ("T1", T1), ("T2", T2), ("T3", T3) })
        {
            if (value is < 1 or > 3600)
                errors.Add($"Iec104 {name} must be between 1 and 3600 seconds.");
        }

        foreach (var client in AllowedClients)
        {
            if (!IPAddress.TryParse(client, out var clientAddress) || clientAddress.AddressFamily != AddressFamily.InterNetwork)
                errors.Add($"Iec104 AllowedClients entry '{client}' must be an IPv4 address.");
        }

        if (string.IsNullOrWhiteSpace(CommandActor))
            errors.Add("Iec104 CommandActor is required.");
        if (CommandRole < AccessRole.Operator)
            errors.Add("Iec104 CommandRole must be Operator or Administrator.");
        return errors;
    }
}
