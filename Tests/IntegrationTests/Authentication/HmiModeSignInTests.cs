using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Api.Authentication;
using Application.Common;
using IntegrationTests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace IntegrationTests.Authentication;

public sealed class HmiModeSignInTests
{
    [Fact]
    public async Task LoginOffersRadioCardsWithWaterSelectedByDefault()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        await using var session = BrowserSession.Open(factory);
        var html = await session.Client.GetStringAsync("/account/login");
        Assert.Contains("Water treatment plant", html);
        Assert.Contains("Electrical distribution grid", html);
        Assert.Contains("name=\"mode\" value=\"Water\" checked", html);
        Assert.Contains("name=\"mode\" value=\"Grid\"", html);
    }

    [Theory]
    [InlineData("Water", "/")]
    [InlineData("Grid", "/grid")]
    public async Task LoginSetsTheModeClaimAndRedirectsToItsOverview(string mode, string destination)
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        await using var session = BrowserSession.Open(factory);
        var response = await SignInAsync(session, mode);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(destination, response.Headers.Location?.OriginalString);
        var ticket = Ticket(factory.Services, response);
        Assert.Equal(mode, ticket.Principal.FindFirst(ScadaClaimTypes.Mode)?.Value);
        Assert.Equal("eva", ticket.Principal.Identity?.Name);
    }

    [Fact]
    public async Task LegacyLoginWithoutModeDefaultsToWater()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        await using var session = BrowserSession.Open(factory);
        var response = await session.SignInAsync("eva", BrowserSession.EvaPassword);
        Assert.Equal("Water", Ticket(factory.Services, response).Principal.FindFirst(ScadaClaimTypes.Mode)?.Value);
    }

    [Fact]
    public async Task FailedLoginRetainsTheChosenMode()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        await using var session = BrowserSession.Open(factory);
        var response = await SignInAsync(session, "Grid", password: "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("name=\"mode\" value=\"Grid\" checked", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UndefinedModeIsRejected()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        await using var session = BrowserSession.Open(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await SignInAsync(session, "17")).StatusCode);
    }

    [Fact]
    public async Task ModeSwitchRequiresCsrfAndPreservesIdentityAndOriginalSessionStart()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        await using var session = BrowserSession.Open(factory);
        var original = Ticket(factory.Services, await SignInAsync(session, "Water"));
        Assert.Equal(HttpStatusCode.BadRequest, await session.PostApiAsync("/account/mode", null));
        Assert.Equal(HttpStatusCode.BadRequest, await session.PostApiAsync("/account/mode", "forged"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/mode");
        request.Headers.Add("X-CSRF-TOKEN", await session.ApiTokenAsync());
        var response = await session.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/grid", response.Headers.Location?.OriginalString);
        var changed = Ticket(factory.Services, response);
        Assert.Equal("Grid", changed.Principal.FindFirst(ScadaClaimTypes.Mode)?.Value);
        Assert.Equal(original.Principal.Identity?.Name, changed.Principal.Identity?.Name);
        Assert.Equal(original.Principal.FindFirst(ScadaClaimTypes.Credential)?.Value, changed.Principal.FindFirst(ScadaClaimTypes.Credential)?.Value);
        Assert.Equal(original.Principal.FindFirst(ScadaClaimTypes.Issued)?.Value, changed.Principal.FindFirst(ScadaClaimTypes.Issued)?.Value);
        Assert.Equal(original.Properties.GetString("scada.session-started-at"), changed.Properties.GetString("scada.session-started-at"));
        using var back = new HttpRequestMessage(HttpMethod.Post, "/account/mode");
        back.Headers.Add("X-CSRF-TOKEN", await session.ApiTokenAsync());
        var water = await session.Client.SendAsync(back);
        Assert.Equal("/", water.Headers.Location?.OriginalString);
        Assert.Equal("Water", Ticket(factory.Services, water).Principal.FindFirst(ScadaClaimTypes.Mode)?.Value);
    }

    [Fact]
    public async Task ModeSwitchRejectsAnAnonymousBrowser()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        await using var session = BrowserSession.Open(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/mode");
        request.Headers.Add("X-CSRF-TOKEN", await session.ApiTokenAsync());
        var response = await session.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/account/login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task RoleRenewalKeepsGridMode()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings());
        await using var session = BrowserSession.Open(factory);
        using var admin = factory.Client(ApiTestFactory.AdminKey);
        await SignInAsync(session, "Grid");
        await admin.PostAsJsonAsync("/api/accounts/eva/role", new { role = "Viewer" });
        var response = await session.Client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ticket = Ticket(factory.Services, response);
        Assert.Equal("Viewer", ticket.Principal.FindFirst(ClaimTypes.Role)?.Value);
        Assert.Equal("Grid", ticket.Principal.FindFirst(ScadaClaimTypes.Mode)?.Value);
    }

    [Fact]
    public Task OidcPrincipalDefaultsToWater()
    {
        var user = new Security.Authentication.AuthenticatedUser("teacher", Application.Identity.AccessRole.Administrator, "oidc:subject");
        var principal = ScadaPrincipalFactory.Create(user, ScadaSignInSources.Oidc, ScadaSignInSources.Oidc);
        Assert.Equal(SimulationMode.Water.ToString(), principal.FindFirst(ScadaClaimTypes.Mode)?.Value);
        return Task.CompletedTask;
    }

    private static async Task<HttpResponseMessage> SignInAsync(BrowserSession session, string mode, string? password = null) =>
        await session.Client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await session.FormTokenAsync(),
            ["username"] = "eva",
            ["password"] = password ?? BrowserSession.EvaPassword,
            ["returnUrl"] = "/",
            ["mode"] = mode
        }));

    private static AuthenticationTicket Ticket(IServiceProvider services, HttpResponseMessage response)
    {
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("scada.session=", StringComparison.Ordinal));
        var value = cookie["scada.session=".Length..].Split(';')[0];
        return services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(ScadaAuthenticationSchemes.Cookie)
            .TicketDataFormat.Unprotect(Uri.UnescapeDataString(value))!;
    }
}
