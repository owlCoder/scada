using System.Net;
using IntegrationTests.Support;
using Xunit;

namespace IntegrationTests.Authentication;

public sealed class AuthorizationAuditTests
{
    [Fact]
    public async Task InsufficientRoleIsForbiddenAndAudited()
    {
        using var factory = ApiTestFactory.Create();
        using var viewer = factory.Client(ApiTestFactory.ViewerKey);
        using var admin = factory.Client(ApiTestFactory.AdminKey);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/simulation/pause", null)).StatusCode);

        var events = await ApiTestFactory.EventuallyAsync(() => admin.GetJsonAsync("/api/events?category=AuthorizationDenied"),
            e => e.GetProperty("totalCount").GetInt32() > 0);
        var denied = events.GetProperty("items").EnumerateArray().First();
        Assert.Equal("legacy-Viewer", denied.GetProperty("actor").GetString());
        Assert.Contains("POST /api/simulation/pause", denied.GetProperty("message").GetString());
        Assert.Contains("requires Operator", denied.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AnonymousAndInvalidCredentialsAreUnauthorizedNotForbidden()
    {
        using var factory = ApiTestFactory.Create();
        using var anonymous = factory.Client(null);
        using var invalid = factory.Client("not-a-key");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/simulation/pause", null)).StatusCode);
        var response = await invalid.PostAsync("/api/simulation/pause", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("ApiKey", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Fact]
    public async Task ApiKeysCanBeTurnedOff()
    {
        using var factory = ApiTestFactory.Create(("Security:ApiKeys:Enabled", "false"));
        using var admin = factory.Client(ApiTestFactory.AdminKey);

        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/me")).StatusCode);
    }
}
