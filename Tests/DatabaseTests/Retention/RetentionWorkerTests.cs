using Application.Events;
using Application.Telemetry;
using DatabaseTests.Support;
using Infrastructure.Persistence.Postgres.Stores;
using Infrastructure.Persistence.Retention;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace DatabaseTests.Retention;

[Collection(PostgresCollection.Name)]
public sealed class RetentionWorkerTests(PostgresContainerFixture postgres)
{
    [Fact]
    public async Task RetentionPurgesOnlyDataOlderThanEachTargetsMaxAge()
    {
        var factory = await (await postgres.CreateDatabaseAsync()).MigratedAsync();
        var events = new PostgresEventStore(factory);
        var telemetry = new PostgresTelemetryStore(factory);
        await events.AppendAsync([Samples.Event("old", seconds: 0), Samples.Event("new", seconds: 7200)], CancellationToken.None);
        await telemetry.AppendAsync([
            new TelemetrySample(1, Samples.Start, TimeSpan.Zero, "LT-101", 40, "Good", "a"),
            new TelemetrySample(2, Samples.Start.AddSeconds(5400), TimeSpan.Zero, "LT-101", 41, "Good", "b")
        ], CancellationToken.None);
        var options = new RetentionOptions { DefaultMaxAge = TimeSpan.FromHours(1), MaxAge = { ["telemetry"] = TimeSpan.FromHours(3) } };
        var time = new FakeTimeProvider(Samples.Start.AddHours(2.5));
        var worker = new RetentionWorker([events, telemetry], options, time, NullLogger<RetentionWorker>.Instance);

        var removed = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, removed["events"]);
        Assert.Equal(0, removed["telemetry"]);
        Assert.Equal("new", (await events.QueryAsync(new EventQuery(), CancellationToken.None)).Items.Single().Message);
        Assert.Equal(2, (await telemetry.QueryAsync(new TelemetryQuery(null, null, null), CancellationToken.None)).Count);
    }

    [Fact]
    public async Task AFailingTargetDoesNotStopTheOthers()
    {
        var factory = await (await postgres.CreateDatabaseAsync()).MigratedAsync();
        var events = new PostgresEventStore(factory);
        await events.AppendAsync([Samples.Event("old")], CancellationToken.None);
        var worker = new RetentionWorker([new BrokenRetentionTarget(), events], new RetentionOptions { DefaultMaxAge = TimeSpan.FromMinutes(1) },
            new FakeTimeProvider(Samples.Start.AddHours(1)), NullLogger<RetentionWorker>.Instance);

        var removed = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, removed["broken"]);
        Assert.Equal(1, removed["events"]);
    }
}
