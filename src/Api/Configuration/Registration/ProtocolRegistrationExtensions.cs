using Api.Authentication;
using Application.Simulation;
using Protocols.Modbus;
using Protocols.OpcUa;

namespace Api.Configuration.Registration;

public static class ProtocolRegistrationExtensions
{
    public static WebApplicationBuilder AddProtocolServices(this WebApplicationBuilder builder)
    {
        return builder.AddModbus().AddOpcUa().AddIec104();
    }

    private static WebApplicationBuilder AddModbus(this WebApplicationBuilder builder)
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

    private static WebApplicationBuilder AddOpcUa(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.Bind<OpcUaOptions>(ConfigurationSections.OpcUa);
        if (!options.Enabled)
            return builder;

        var errors = options.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        options.PkiDirectory = options.ResolvePkiDirectory(builder.Environment.ContentRootPath);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IOpcUaIdentityValidator, OpcUaIdentityValidator>();
        builder.Services.AddSingleton<OpcUaServerHost>();
        builder.Services.AddSingleton<ISimulationObserver>(sp => sp.GetRequiredService<OpcUaServerHost>());
        return builder;
    }
}
