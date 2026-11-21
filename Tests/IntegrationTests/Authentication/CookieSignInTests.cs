using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTests.Support;
using Xunit;

namespace IntegrationTests.Authentication;

public sealed class CookieSignInTests
{
    [Fact]
    public async Task LoginPageServesAFormWithAnAntiforgeryToken()
    {
        using var factory = ApiTestFactory.Create();
        await using var session = BrowserSession.Open(factory);

        var response = await session.Client.GetAsync("/account/login?returnUrl=/alarms");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("action=\"/account/login\"", html);
        Assert.Contains("value=\"/alarms\"", html);
        Assert.False(string.IsNullOrEmpty(await session.FormTokenAsync()));
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignInIssuesAHardenedSessionCookie()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        await using var session = BrowserSession.Open(factory);

        var response = await session.SignInAsync("EVA", BrowserSession.EvaPassword, "/alarms");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/alarms", response.Headers.Location?.OriginalString);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("scada.session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        var me = JsonDocument.Parse(await session.Client.GetStringAsync("/api/me")).RootElement;
        Assert.Equal("eva", me.GetProperty("username").GetString());
        Assert.Equal("Operator", me.GetProperty("role").GetString());
    }

    [Theory]
    [InlineData("//evil.example/phish")]
    [InlineData("https://evil.example/")]
    [InlineData("/\\evil.example")]
    public async Task ReturnUrlsOutsideTheSiteAreIgnored(string returnUrl)
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        await using var session = BrowserSession.Open(factory);

        var response = await session.SignInAsync("eva", BrowserSession.EvaPassword, returnUrl);

        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task SignInWithoutAnAntiforgeryTokenIsRejected()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        await using var session = BrowserSession.Open(factory);

        var response = await session.SignInAsync("eva", BrowserSession.EvaPassword, token: "forged");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.Client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task FailedSignInsShowOneGenericMessageAndLockTheAccount()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings(("Security:Lockout:MaxFailedAttempts", "3")));
        await using var session = BrowserSession.Open(factory);
        using var admin = factory.Client(ApiTestFactory.AdminKey);

        var unknown = await (await session.SignInAsync("nobody", "whatever-password")).Content.ReadAsStringAsync();
        for (var i = 0; i < 3; i++)
        {
            var wrong = await session.SignInAsync("eva", "wrong-password-123");
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Contains("Invalid username or password.", await wrong.Content.ReadAsStringAsync());
        }

        Assert.Contains("Invalid username or password.", unknown);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.SignInAsync("eva", BrowserSession.EvaPassword)).StatusCode);
        var events = await admin.GetJsonAsync("/api/events?category=Authentication&pageSize=50");
        Assert.Contains(events.GetProperty("items").EnumerateArray(), e => e.GetProperty("message").GetString()!.Contains("locked after 3"));
        Assert.DoesNotContain(events.GetProperty("items").EnumerateArray(),
            e => e.GetProperty("message").GetString()!.Contains("wrong-password", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CookieSessionsNeedAnAntiforgeryTokenForApiWrites()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        await using var session = BrowserSession.Open(factory);
        await session.SignInAsync("eva", BrowserSession.EvaPassword);

        Assert.Equal(HttpStatusCode.BadRequest, await session.PostApiAsync("/api/plant/devices/V-101/mode/manual", null));
        Assert.Equal(HttpStatusCode.BadRequest, await session.PostApiAsync("/api/plant/devices/V-101/mode/manual", "forged"));
        Assert.Equal(HttpStatusCode.OK, await session.PostApiAsync("/api/plant/devices/V-101/mode/manual", await session.ApiTokenAsync()));
    }

    [Fact]
    public async Task DisablingTheAccountOrRotatingItsPasswordEndsTheSession()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        using var admin = factory.Client(ApiTestFactory.AdminKey);
        await using var first = BrowserSession.Open(factory);
        await using var second = BrowserSession.Open(factory);
        await first.SignInAsync("eva", BrowserSession.EvaPassword);
        await second.SignInAsync("eva", BrowserSession.EvaPassword);

        await admin.PostAsJsonAsync("/api/accounts/eva/enabled", new { enabled = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.Client.GetAsync("/api/me")).StatusCode);

        await admin.PostAsJsonAsync("/api/accounts/eva/enabled", new { enabled = true });
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await admin.PostAsync("/api/accounts/eva/password/rotate", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.Client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task RoleChangesApplyToARunningSession()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        using var admin = factory.Client(ApiTestFactory.AdminKey);
        await using var session = BrowserSession.Open(factory);
        await session.SignInAsync("eva", BrowserSession.EvaPassword);

        await admin.PostAsJsonAsync("/api/accounts/eva/role", new { role = "Viewer" });

        Assert.Equal("Viewer", JsonDocument.Parse(await session.Client.GetStringAsync("/api/me")).RootElement.GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, await session.PostApiAsync("/api/plant/devices/V-101/mode/manual", await session.ApiTokenAsync()));
    }

    [Fact]
    public async Task LogoutEndsTheSession()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        await using var session = BrowserSession.Open(factory);
        await session.SignInAsync("eva", BrowserSession.EvaPassword);
        var token = await session.FormTokenAsync();

        var response = await session.Client.PostAsync("/account/logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.Client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task LogoutWithoutAnAntiforgeryTokenIsRejected()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        await using var session = BrowserSession.Open(factory);
        await session.SignInAsync("eva", BrowserSession.EvaPassword);

        var response = await session.Client.PostAsync("/account/logout", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/me")).StatusCode);
    }
}
