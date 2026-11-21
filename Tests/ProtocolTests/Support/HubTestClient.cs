using System.Text.Json;
using IntegrationTests.Support;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;

namespace ProtocolTests.Support;

internal static class HubTestClient
{
    public static WebApplicationFactory<Program> CreateHost() => ApiTestFactory.Create(
        ("Simulation:Host:RunLoop", "false"), ("RateLimiting:PermitsPerMinute", "100000"),
        ("Serilog:MinimumLevel:Default", "Error"));

    public static HubConnection Connect(WebApplicationFactory<Program> factory, string? key, Func<HttpMessageHandler, HttpMessageHandler>? handler = null)
    {
        var server = factory.Server;
        return new HubConnectionBuilder().WithUrl(new Uri(server.BaseAddress, "hubs/scada"), options =>
        {
            options.HttpMessageHandlerFactory = _ => handler?.Invoke(server.CreateHandler()) ?? server.CreateHandler();
            options.Transports = HttpTransportType.LongPolling;
            if (key is not null)
                options.Headers.Add("X-Api-Key", key);
        }).Build();
    }

    public static JsonElement Sensor(JsonElement plant) => plant.GetProperty("sensors").EnumerateArray()
        .Single(sensor => sensor.GetProperty("id").GetString() == "LT-101");
}
