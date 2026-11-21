using System.Net;
using Api.Authentication;
using IntegrationTests.Support;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace IntegrationTests.Authentication;

public sealed class ProxySecurityTests
{
    [Theory]
    [InlineData(true, "192.0.2.10", true)]
    [InlineData(true, "127.0.0.1", false)]
    [InlineData(false, "192.0.2.10", false)]
    public async Task OnlyConfiguredPeersControlTheClientIpAndScheme(bool configured, string peer, bool trusted)
    {
        (string, string)[] settings = configured
            ? [("RateLimiting:PermitsPerMinute", "1"), ("ForwardedHeaders:KnownProxies:0", "192.0.2.10")]
            : [("RateLimiting:PermitsPerMinute", "1")];
        using var factory = ApiTestFactory.Create(settings);

        var first = await factory.Server.SendAsync(context => ForwardedRequest(context, peer, "198.51.100.1"));
        var second = await factory.Server.SendAsync(context => ForwardedRequest(context, peer, "198.51.100.2"));
        var repeated = await factory.Server.SendAsync(context => ForwardedRequest(context, peer, "198.51.100.1"));

        Assert.Equal(StatusCodes.Status200OK, first.Response.StatusCode);
        Assert.Equal(trusted, first.Response.Headers.SetCookie.ToString().Contains("; secure", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(trusted ? StatusCodes.Status200OK : StatusCodes.Status429TooManyRequests, second.Response.StatusCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, repeated.Response.StatusCode);
    }

    [Fact]
    public async Task RequireSecureSetsBothCookiePoliciesToAlways()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings(("Security:Cookies:RequireSecure", "true")));
        await using var session = new BrowserSession(factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        }));

        using var page = await session.Client.GetAsync("/account/login");
        using var signIn = await session.SignInAsync("eva", BrowserSession.EvaPassword);
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Contains("; secure", page.Headers.GetValues("Set-Cookie").Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; secure", signIn.Headers.GetValues("Set-Cookie").Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CookieSecurePolicy.Always, factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(ScadaAuthenticationSchemes.Cookie).Cookie.SecurePolicy);
        Assert.Equal(CookieSecurePolicy.Always, factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.SecurePolicy);
    }

    private static HttpContext ForwardedRequest(HttpContext context, string peer, string client)
    {
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Scheme = "http";
        context.Request.Path = "/account/antiforgery";
        context.Request.Headers["X-Forwarded-For"] = client;
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        return context;
    }
}
