using Application.Grid;
using Protocols.Iec104;

namespace Api.Configuration.Registration;

/// <summary>
/// IEC 60870-5-104 slave for the grid mode: options bound from <c>Protocols:Iec104</c>, and when enabled the address
/// map, ASDU handler, listener and the grid observer that sends spontaneous data. Requires <see cref="IGridView"/> and
/// <see cref="IGridControl"/> from the grid engine.
/// </summary>
public static class Iec104RegistrationExtensions
{
    public static WebApplicationBuilder AddIec104(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var options = configuration.Bind<Iec104Options>(ConfigurationSections.Iec104);
        builder.Services.AddSingleton(options);
        if (!options.Enabled)
            return builder;

        var errors = options.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        builder.Services.AddSingleton(sp => Iec104AddressMap.Build(sp.GetRequiredService<IGridView>().Definition));
        builder.Services.AddSingleton<Iec104AsduHandler>();
        builder.Services.AddSingleton<Iec104Server>();
        builder.Services.AddSingleton<Iec104GridMonitor>();
        builder.Services.AddSingleton<IGridObserver>(sp => sp.GetRequiredService<Iec104GridMonitor>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Iec104Server>());
        return builder;
    }
}
