using IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PerformanceTests.Support;

internal static class PerformanceHost
{
    public static WebApplicationFactory<Program> Create(FakeTimeProvider? time = null) => ApiTestFactory.Create(
        ("Simulation:Host:RunLoop", "false"), ("Simulation:StartPaused", "true"),
        ("RateLimiting:PermitsPerMinute", "100000"), ("Serilog:MinimumLevel:Default", "Error"))
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(time ?? new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        }));
}
