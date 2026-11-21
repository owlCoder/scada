using System.Net;
using IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace IntegrationTests.Authentication;

public sealed class SessionLifetimeTests
{
    [Theory]
    [InlineData(false, 50)]
    [InlineData(true, 480)]
    public async Task SlidingRenewalCannotExtendTheAbsoluteLifetime(bool oidc, int lifetimeMinutes)
    {
        var time = new FakeTimeProvider();
        var settings = oidc
            ? BrowserSession.EvaSettings(("Simulation:Host:RunLoop", "false"))
            : BrowserSession.EvaSettings(("Simulation:Host:RunLoop", "false"), ("Security:Session:AbsoluteLifetime", "00:50:00"));
        using var baseline = ApiTestFactory.Create(settings);
        using var factory = baseline.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(time);
            if (oidc)
                services.AddSingleton<IStartupFilter, OidcSessionStartupFilter>();
        }));
        await using var session = BrowserSession.Open(factory);
        if (oidc)
            Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/test/oidc-session")).StatusCode);
        else
            Assert.Equal(HttpStatusCode.Redirect, (await session.SignInAsync("eva", BrowserSession.EvaPassword)).StatusCode);

        var elapsed = 0;
        while (elapsed + 16 < lifetimeMinutes)
        {
            time.Advance(TimeSpan.FromMinutes(16));
            elapsed += 16;

            using var response = await session.Client.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("scada.session=", StringComparison.Ordinal));
        }

        time.Advance(TimeSpan.FromMinutes(lifetimeMinutes - elapsed));
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.Client.GetAsync("/api/me")).StatusCode);
    }
}
