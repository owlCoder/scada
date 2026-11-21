using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using Application.Diagnostics;
using IntegrationTests.Support;
using Xunit;

namespace IntegrationTests.Observability;

public sealed class CorrelationAndMetricsTests
{
    [Fact]
    public async Task CorrelationIdIsEchoedAndAttachedToEvents()
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.OperatorKey);
        using var admin = factory.Client(ApiTestFactory.AdminKey);
        var correlationId = "test-" + Guid.NewGuid().ToString("N")[..12];

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/plant/devices/V-101/mode/manual");
        request.Headers.Add("X-Correlation-Id", correlationId);
        var response = await client.SendAsync(request);

        Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-Id").Single());
        var events = await ApiTestFactory.EventuallyAsync(() => admin.GetJsonAsync("/api/events?pageSize=200"),
            e => e.GetProperty("items").EnumerateArray().Any(i => i.GetProperty("correlationId").GetString() == correlationId));
        Assert.Contains(events.GetProperty("items").EnumerateArray(), i => i.GetProperty("correlationId").GetString() == correlationId);
    }

    [Theory]
    [InlineData("contains spaces")]
    [InlineData("<script>")]
    [InlineData("")]
    public async Task MalformedCorrelationIdsAreReplaced(string supplied)
    {
        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(null);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", supplied);

        var echoed = (await client.SendAsync(request)).Headers.GetValues("X-Correlation-Id").Single();

        Assert.NotEqual(supplied, echoed);
        Assert.Matches("^[A-Za-z0-9._-]{1,64}$", echoed);
    }

    [Fact]
    public async Task CommandsAreCountedByOutcome()
    {
        var measurements = new List<(long Value, string? Outcome)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ScadaInstrumentation.MeterName && instrument.Name == "scada.commands")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? outcome = null;
            foreach (var tag in tags)
                if (tag.Key == "outcome")
                    outcome = tag.Value as string;
            lock (measurements)
                measurements.Add((value, outcome));
        });
        listener.Start();

        using var factory = ApiTestFactory.Create();
        using var client = factory.Client(ApiTestFactory.OperatorKey);
        await client.PostAsync("/api/plant/devices/V-101/mode/manual", null);
        await client.PostAsJsonAsync("/api/commands", new { deviceId = "NO-SUCH", type = "StartPump" });

        lock (measurements)
        {
            Assert.Contains(measurements, m => m.Outcome == "accepted");
            Assert.Contains(measurements, m => m.Outcome == "rejected");
        }
    }

    [Fact]
    public async Task HealthAndMetricsRegistrationDoNotBreakStartupWithoutAnExporter()
    {
        using var factory = ApiTestFactory.Create(("Observability:JsonLogs", "true"));
        using var client = factory.Client(null);

        Assert.True((await client.GetAsync("/health/live")).IsSuccessStatusCode);
    }
}
