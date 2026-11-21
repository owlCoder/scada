using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Support;
using Xunit;

namespace IntegrationTests.Api;

public sealed class AdministrationApiTests
{
    [Fact]
    public async Task AdministratorCanTuneDetectionRules()
    {
        using var factory = ApiTestFactory.Create();
        using var admin = factory.Client(ApiTestFactory.AdminKey);
        var settings = new { enabled = false, severity = "Low", threshold = 5, windowSeconds = 120, cooldownSeconds = 60 };

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/detections/rules/command-replay", settings)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/detections/rules/no-such-rule", settings)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/detections/rules/command-replay",
            settings with { windowSeconds = 0 })).StatusCode);

        var rules = await admin.GetJsonAsync("/api/detections/rules");
        var replay = rules.EnumerateArray().Single(r => r.GetProperty("id").GetString() == "command-replay");
        Assert.False(replay.GetProperty("settings").GetProperty("enabled").GetBoolean());
        Assert.Equal(17, rules.GetArrayLength());
    }

    [Fact]
    public async Task ConfiguredUsersAreSeededAsAccountsAndManagedByAdministrators()
    {
        using var factory = ApiTestFactory.Create();
        using var admin = factory.Client(ApiTestFactory.AdminKey);

        var accounts = await ApiTestFactory.EventuallyAsync(() => admin.GetJsonAsync("/api/accounts"), a => a.GetArrayLength() > 0);
        var ana = accounts.EnumerateArray().Single(a => a.GetProperty("username").GetString() == "ana");
        Assert.Equal("Operator", ana.GetProperty("role").GetString());
        Assert.True(ana.GetProperty("hasApiKey").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/accounts/ana/role", new { role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/accounts/nobody/role", new { role = "Viewer" })).StatusCode);
        var rotated = await (await admin.PostAsync("/api/accounts/ana/password/rotate", null)).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.False(string.IsNullOrEmpty(rotated.GetProperty("secret").GetString()));
    }

    [Fact]
    public async Task InstructorCanManipulateTheTrainingNetworkAndControllers()
    {
        using var factory = ApiTestFactory.Create();
        using var admin = factory.Client(ApiTestFactory.AdminKey);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/network/links/L-PLC2-IO/disconnect", null)).StatusCode);
        var network = await admin.GetJsonAsync("/api/network");
        var link = network.GetProperty("links").EnumerateArray().Single(l => l.GetProperty("id").GetString() == "L-PLC2-IO");
        Assert.Equal("Down", link.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/network/links/L-PLC2-IO/connect", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/controllers/PLC-2/stop", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/controllers/PLC-9/stop", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/controllers/PLC-2/explode", null)).StatusCode);
        var controllers = await admin.GetJsonAsync("/api/controllers");
        Assert.Equal("Stopped", controllers.EnumerateArray().Single(c => c.GetProperty("id").GetString() == "PLC-2").GetProperty("state").GetString());
    }

    [Fact]
    public async Task SimulationCanBePausedSteppedAndReset()
    {
        using var factory = ApiTestFactory.Create();
        using var admin = factory.Client(ApiTestFactory.AdminKey);

        var paused = await (await admin.PostAsync("/api/simulation/pause", null)).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(paused.GetProperty("isPaused").GetBoolean());
        var before = (await admin.GetJsonAsync("/api/simulation")).GetProperty("stepCount").GetInt64();
        var stepped = await (await admin.PostAsJsonAsync("/api/simulation/step", new { count = 4 })).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(4, stepped.GetProperty("executed").GetInt32());
        Assert.Equal(before + 4, (await admin.GetJsonAsync("/api/simulation")).GetProperty("stepCount").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/simulation/step", new { count = 1001 })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/simulation/reset", null)).StatusCode);
        Assert.Equal(0, (await admin.GetJsonAsync("/api/simulation")).GetProperty("stepCount").GetInt64());
    }
}
