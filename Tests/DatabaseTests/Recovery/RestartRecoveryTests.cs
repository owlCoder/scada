using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DatabaseTests.Support;
using Infrastructure.Persistence.Postgres;
using Xunit;

namespace DatabaseTests.Recovery;

/// <summary>Runs the API on PostgreSQL, stops it, starts a new instance on the same database and checks what survived.</summary>
[Collection(PostgresCollection.Name)]
public sealed class RestartRecoveryTests(PostgresContainerFixture postgres)
{
    [Fact]
    public async Task StateSurvivesAnApiRestart()
    {
        var database = await postgres.CreateDatabaseAsync();
        string[] eventIds;
        long stepsBeforeRestart;

        using (var first = DatabaseApiFactory.Create(database))
        {
            using var admin = first.Client(DatabaseApiFactory.AdminKey);
            using var @operator = first.Client(DatabaseApiFactory.OperatorKey);

            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/health/ready")).StatusCode);
            var health = await admin.GetFromJsonAsync<JsonElement>("/health");
            Assert.Equal("postgres", health.GetProperty("storage").GetString());

            var settings = new { enabled = false, severity = "Low", threshold = 5, windowSeconds = 120, cooldownSeconds = 60 };
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/detections/rules/command-replay", settings)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/accounts/ana/role", new { role = "Viewer" })).StatusCode);
            Assert.True((await @operator.PostAsync("/api/plant/devices/V-101/mode/manual", null)).IsSuccessStatusCode);
            Assert.True((await @operator.PostAsJsonAsync("/api/plant/valve/V-101/command", new { open = false })).IsSuccessStatusCode);

            var events = await DatabaseApiFactory.EventuallyAsync(() => admin.GetFromJsonAsync<JsonElement>("/api/events?pageSize=200"),
                e => e.GetProperty("totalCount").GetInt32() >= 3);
            eventIds = events.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToArray();
            Assert.NotEmpty(eventIds);
            // The valve travels gradually; wait until it is fully closed so the restored position is deterministic.
            var closed = await DatabaseApiFactory.EventuallyAsync(() => admin.GetFromJsonAsync<JsonElement>("/api/plant"),
                p => V101(p).GetProperty("position").GetDouble() == 0, TimeSpan.FromSeconds(60));
            Assert.Equal(0, V101(closed).GetProperty("position").GetDouble());
            stepsBeforeRestart = closed.GetProperty("stepCount").GetInt64();
        }

        using var second = DatabaseApiFactory.Create(database);
        using var admin2 = second.Client(DatabaseApiFactory.AdminKey);

        var plant = await admin2.GetFromJsonAsync<JsonElement>("/api/plant");
        var valve = V101(plant);
        Assert.Equal("Manual", valve.GetProperty("mode").GetString());
        Assert.Equal(0, valve.GetProperty("setpoint").GetDouble());
        Assert.Equal(0, valve.GetProperty("position").GetDouble());
        Assert.True(plant.GetProperty("stepCount").GetInt64() >= stepsBeforeRestart, "The simulation restarted from scratch.");

        var rules = await admin2.GetFromJsonAsync<JsonElement>("/api/detections/rules");
        var replay = rules.EnumerateArray().Single(r => r.GetProperty("id").GetString() == "command-replay");
        Assert.False(replay.GetProperty("settings").GetProperty("enabled").GetBoolean());
        Assert.Equal(5, replay.GetProperty("settings").GetProperty("threshold").GetDouble());

        var accounts = await admin2.GetFromJsonAsync<JsonElement>("/api/accounts");
        var ana = accounts.EnumerateArray().Single(a => a.GetProperty("username").GetString() == "ana");
        Assert.Equal("Viewer", ana.GetProperty("role").GetString());

        var restoredEvents = await admin2.GetFromJsonAsync<JsonElement>("/api/events?pageSize=200");
        var restoredIds = restoredEvents.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToHashSet();
        Assert.All(eventIds, id => Assert.Contains(id, restoredIds));
    }

    [Fact]
    public async Task StorageProbeDistinguishesReachableAndUnreachableDatabases()
    {
        var database = await postgres.CreateDatabaseAsync();
        var unreachable = new TestDatabase("Host=127.0.0.1;Port=1;Database=scada;Username=scada;Password=x;Timeout=1");

        Assert.True(await new PostgresStorageProbe(database.ContextFactory()).IsAvailableAsync(CancellationToken.None));
        Assert.False(await new PostgresStorageProbe(unreachable.ContextFactory()).IsAvailableAsync(CancellationToken.None));
    }

    private static JsonElement V101(JsonElement plant) =>
        plant.GetProperty("valves").EnumerateArray().Single(v => v.GetProperty("id").GetString() == "V-101");
}
