using System.Net;
using Application.Events;
using IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntegrationTests.Authentication;

public sealed class RateLimitTests
{
    [Fact]
    public async Task RequestsAboveTheLimitGet429WithRetryAfterAndAreAuditedOnce()
    {
        using var factory = ApiTestFactory.Create(("RateLimiting:PermitsPerMinute", "5"));
        using var viewer = factory.Client(ApiTestFactory.ViewerKey);
        using var anonymous = factory.Client(null);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++)
            statuses.Add((await viewer.GetAsync("/api/me")).StatusCode);

        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.OK));
        var limited = await viewer.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", limited.Headers.GetValues("Retry-After").Single());
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/live")).StatusCode);

        var buffer = factory.Services.GetRequiredService<RecentEventBuffer>();
        Assert.Single(buffer.GetRecent(), e => e.Message.StartsWith("Rate limit exceeded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SignInAttemptsAreLimitedPerClient()
    {
        using var factory = ApiTestFactory.Create(BrowserSession.EvaSettings(("RateLimiting:LoginPermitsPerMinute", "2")));
        await using var session = BrowserSession.Open(factory);
        var token = await session.FormTokenAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await session.SignInAsync("eva", "wrong-password-1", token: token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.SignInAsync("eva", "wrong-password-2", token: token)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await session.SignInAsync("eva", BrowserSession.EvaPassword, token: token)).StatusCode);
    }
}
