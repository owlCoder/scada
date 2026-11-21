using System.Diagnostics;
using System.Text.Json;
using IntegrationTests.Support;
using PerformanceTests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTests.Api;

[Trait("Category", "Performance")]
public sealed class RestPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AuthenticatedReadEndpointsSustainConcurrentRequests()
    {
        await using var host = PerformanceHost.Create();
        using var client = host.Client(ApiTestFactory.ViewerKey);
        string[] paths = ["/api/plant", "/api/controllers", "/api/network", "/api/alarms", "/api/scenarios"];
        foreach (var path in paths)
        {
            using var warmup = await client.GetAsync(path);
            warmup.EnsureSuccessStatusCode();
        }
        const int concurrency = 10;
        const int perWorker = 50;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var watch = Stopwatch.StartNew();
        var bytes = await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async worker =>
        {
            long received = 0;
            for (var index = 0; index < perWorker; index++)
            {
                using var response = await client.GetAsync(paths[(worker + index) % paths.Length], timeout.Token);
                Assert.True(response.IsSuccessStatusCode, $"Read returned {response.StatusCode}.");
                var body = await response.Content.ReadAsByteArrayAsync(timeout.Token);
                using var json = JsonDocument.Parse(body);
                Assert.Contains(json.RootElement.ValueKind, new[] { JsonValueKind.Object, JsonValueKind.Array });
                received += body.Length;
            }
            return received;
        }));
        watch.Stop();
        var requests = concurrency * perWorker;
        output.WriteLine("REST: {0} authenticated reads ({1} concurrent) in {2:F1} ms; {3:F0} requests/s; {4:N0} response bytes.",
            requests, concurrency, watch.Elapsed.TotalMilliseconds, requests / watch.Elapsed.TotalSeconds, bytes.Sum());
        Assert.True(bytes.Sum() > requests * 2);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"500 REST reads took {watch.Elapsed}.");
    }
}
