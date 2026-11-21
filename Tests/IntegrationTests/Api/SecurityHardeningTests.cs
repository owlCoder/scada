using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Persistence;
using IntegrationTests.Support;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace IntegrationTests.Api;

public sealed class SecurityHardeningTests
{
    [Fact]
    public async Task RotatedOrRevokedKeysStopWorkingImmediately()
    {
        using var factory = ApiTestFactory.Create();
        using var admin = factory.Client(ApiTestFactory.AdminKey);
        using var ana = factory.Client(ApiTestFactory.AnaKey);
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/api/me")).StatusCode);

        var rotated = await (await admin.PostAsync("/api/accounts/ana/api-key/rotate", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Unauthorized, (await ana.GetAsync("/api/me")).StatusCode);

        using var renewed = factory.Client(rotated.GetProperty("secret").GetString());
        Assert.Equal("ana", (await renewed.GetJsonAsync("/api/me")).GetProperty("username").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/accounts/ana/enabled", new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await renewed.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task RoleChangesApplyToTheNextRequest()
    {
        using var factory = ApiTestFactory.Create();
        using var admin = factory.Client(ApiTestFactory.AdminKey);
        using var ana = factory.Client(ApiTestFactory.AnaKey);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsync("/api/plant/devices/V-301/mode/manual", null)).StatusCode);

        await admin.PostAsJsonAsync("/api/accounts/ana/role", new { role = "Viewer" });

        Assert.Equal(HttpStatusCode.Unauthorized, (await ana.PostAsync("/api/plant/devices/V-301/mode/automatic", null)).StatusCode);
        Assert.Equal("Viewer", (await ana.GetJsonAsync("/api/me")).GetProperty("role").GetString());
    }

    [Fact]
    public async Task RepeatedInvalidKeysAreAuditedAndThrottled()
    {
        using var factory = ApiTestFactory.Create();
        for (var i = 0; i < 10; i++)
        {
            using var guess = factory.Client($"guess-{i}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await guess.GetAsync("/api/plant")).StatusCode);
        }

        using var valid = factory.Client(ApiTestFactory.AdminKey);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await valid.GetAsync("/api/plant")).StatusCode);
    }

    [Fact]
    public async Task ViewersDoNotSeeGroundTruthButInstructorsDo()
    {
        using var factory = ApiTestFactory.Create();
        using var admin = factory.Client(ApiTestFactory.AdminKey);
        using var viewer = factory.Client(ApiTestFactory.ViewerKey);
        await admin.PostAsync("/api/scenarios/sensor-spoofing/start", null);

        var truth = await ApiTestFactory.EventuallyAsync(() => admin.GetJsonAsync("/api/plant/devices/LT-102"),
            s => s.GetProperty("isSpoofed").GetBoolean());
        var view = await viewer.GetJsonAsync("/api/plant/devices/LT-102");

        Assert.True(truth.GetProperty("isSpoofed").GetBoolean());
        Assert.False(view.GetProperty("isSpoofed").GetBoolean());
        Assert.Equal(view.GetProperty("reportedValue").GetDouble(), view.GetProperty("trueValue").GetDouble());
        var tank = (await viewer.GetJsonAsync("/api/plant")).GetProperty("tanks").EnumerateArray().Single(t => t.GetProperty("id").GetString() == "T-102");
        Assert.Equal(30, tank.GetProperty("levelPercent").GetDouble(), 1);
    }

    [Fact]
    public async Task HealthExposesNoErrorDetailsAndResponsesCarryACsp()
    {
        using var factory = ApiTestFactory.Create();
        using var anonymous = factory.Client(null);

        var response = await anonymous.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("lastError", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task ReadinessFailsWithoutLeakingErrorsWhenStorageIsUnreachable()
    {
        using var factory = ApiTestFactory.Create().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IStorageProbe>();
            services.AddSingleton<IStorageProbe, UnavailableStorageProbe>();
        }));
        using var anonymous = factory.Client(null);

        var response = await anonymous.GetAsync("/health/ready");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("degraded", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("storage").GetProperty("available").GetBoolean());
        Assert.DoesNotContain("secret", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task ReleasingForcesOnAnUnknownControllerIsNotFound()
    {
        using var factory = ApiTestFactory.Create();
        using var admin = factory.Client(ApiTestFactory.AdminKey);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/controllers/PLC-9/release-forces", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/controllers/PLC-1/release-forces", null)).StatusCode);
    }
}
