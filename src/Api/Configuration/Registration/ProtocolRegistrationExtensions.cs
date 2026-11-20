using Protocols;

namespace Api.Configuration.Registration;

public static class ProtocolRegistrationExtensions
{
    public static WebApplicationBuilder AddProtocolServices(this WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue<bool>("Protocols:ModbusEnabled"))
            return builder;

        var port = builder.Configuration.GetValue<int?>("Protocols:ModbusPort") ?? 1502;
        if (port is < 1024 or > 65535)
            throw new InvalidOperationException("Modbus port must be between 1024 and 65535.");

        builder.Services.AddSingleton<IHostedService>(services =>
            new ModbusTcpService(services.GetRequiredService<ModbusFrameService>(),
                services.GetRequiredService<ILogger<ModbusTcpService>>(), port));
        return builder;
    }
}
