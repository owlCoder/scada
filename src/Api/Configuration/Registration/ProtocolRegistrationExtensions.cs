using Protocols.Modbus;

namespace Api.Configuration.Registration;

public static class ProtocolRegistrationExtensions
{
    public static WebApplicationBuilder AddProtocolServices(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var options = configuration.Bind<ModbusOptions>(ConfigurationSections.Modbus);
        // Legacy keys from earlier releases remain supported.
        if (configuration.GetValue<bool?>("Protocols:ModbusEnabled") is { } legacyEnabled)
            options.Enabled = legacyEnabled;
        if (configuration.GetValue<int?>("Protocols:ModbusPort") is { } legacyPort)
            options.Port = legacyPort;

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ModbusRequestProcessor>();
        if (!options.Enabled)
            return builder;

        var errors = options.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
        builder.Services.AddSingleton<ModbusTcpServer>();
        return builder;
    }
}
