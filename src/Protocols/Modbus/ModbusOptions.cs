using System.Net;
using System.Net.Sockets;

namespace Protocols.Modbus;

public sealed class ModbusOptions
{
    public bool Enabled { get; set; }
    public string BindAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1502;
    public int MaxConnections { get; set; } = 16;
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Modbus has no authentication, so accepted writes (only when register writes are enabled) are attributed to
    /// this local operator identity.
    /// </summary>
    public string WriteActor { get; set; } = "modbus-local";

    /// <summary>
    /// Modbus writes need this explicit opt-in in addition to <c>Registers:WritesEnabled</c>, because the protocol cannot
    /// authenticate the client: anything able to reach the configured port could otherwise change setpoints.
    /// </summary>
    public bool AllowWrites { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!IPAddress.TryParse(BindAddress, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            errors.Add("Modbus BindAddress must be an IPv4 address.");
        if (Port is < 1024 or > 65535)
            errors.Add("Modbus port must be between 1024 and 65535.");
        if (MaxConnections is < 1 or > 256)
            errors.Add("Modbus MaxConnections must be between 1 and 256.");
        if (IdleTimeout <= TimeSpan.Zero || IdleTimeout > TimeSpan.FromMinutes(10))
            errors.Add("Modbus IdleTimeout must be between 0 and 10 minutes.");
        if (string.IsNullOrWhiteSpace(WriteActor))
            errors.Add("Modbus WriteActor is required.");
        return errors;
    }
}
