using System.Diagnostics;
using IntegrationTests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceTests.Protocols;

[Trait("Category", "Performance")]
public sealed class ModbusPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task FiftyConcurrentClientsComplete200ReadsEachWithinLatencyBudget()
    {
        await using var host = await ModbusTestHost.StartAsync(("Protocols:Modbus:MaxConnections", "64"));
        const int clients = 50;
        const int reads = 200;
        var connections = await Task.WhenAll(Enumerable.Range(0, clients).Select(_ => RawModbusClient.ConnectAsync(host.Port)));
        try
        {
            // Warm every connection before starting the common clock.
            await Task.WhenAll(connections.Select(connection => connection.ExchangeAsync(1, RawModbusClient.Read())));
            var watch = Stopwatch.StartNew();
            var measurements = await Task.WhenAll(connections.Select(async (connection, index) =>
            {
                var latencies = new double[reads];
                for (ushort request = 0; request < reads; request++)
                {
                    var start = Stopwatch.GetTimestamp();
                    var response = await connection.ExchangeAsync((byte)(index % 3 + 1), RawModbusClient.Read(count: 1), request);
                    latencies[request] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var expected = (index % 3) switch
                    {
                        0 => new byte[] { 3, 2, 1, 144 },
                        1 => new byte[] { 3, 2, 1, 194 },
                        _ => new byte[] { 3, 2, 0, 0 }
                    };
                    Assert.Equal(expected, response);
                }
                return latencies;
            })).WaitAsync(TimeSpan.FromSeconds(8));
            watch.Stop();
            var sorted = measurements.SelectMany(values => values).Order().ToArray();
            var p95 = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1];
            output.WriteLine("Modbus: {0} clients x {1} reads = {2} in {3:F1} ms; {4:F0} reads/s; p50 {5:F2} ms, p95 {6:F2} ms, max {7:F2} ms.",
                clients, reads, sorted.Length, watch.Elapsed.TotalMilliseconds, sorted.Length / watch.Elapsed.TotalSeconds,
                sorted[sorted.Length / 2], p95, sorted[^1]);
            Assert.Equal(clients * reads, sorted.Length);
            Assert.True(p95 < 500, $"Modbus p95 {p95:F2} ms exceeded 500 ms.");
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"10000 reads took {watch.Elapsed}.");
        }
        finally
        {
            foreach (var connection in connections)
                await connection.DisposeAsync();
        }
    }
}
