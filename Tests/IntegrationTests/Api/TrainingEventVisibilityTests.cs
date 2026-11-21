using Application.Events;
using IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntegrationTests.Api;

public sealed class TrainingEventVisibilityTests
{
    [Fact]
    public async Task ScenarioEventsAreVisibleToAdministratorsOnly()
    {
        using var factory = ApiTestFactory.Create();
        var events = factory.Services.GetRequiredService<IEventPublisher>();
        var now = DateTimeOffset.UtcNow;
        events.Publish(SecurityEvent.Create(now, EventCategories.Scenario, EventSeverity.Information, "grid:training",
            "Inject ThreePhaseShortCircuit on F1-1", "instructor", "F1-1"));
        events.Publish(SecurityEvent.Create(now, EventCategories.Audit, EventSeverity.Information, "tests",
            "Ordinary audit entry", "eva"));

        using var viewer = factory.Client(ApiTestFactory.ViewerKey);
        using var admin = factory.Client(ApiTestFactory.AdminKey);

        Assert.DoesNotContain("ThreePhaseShortCircuit", await viewer.GetStringAsync("/api/events/recent?count=500"));
        Assert.Contains("Ordinary audit entry", await viewer.GetStringAsync("/api/events/recent?count=500"));
        Assert.Contains("ThreePhaseShortCircuit", await admin.GetStringAsync("/api/events/recent?count=500"));

        await WaitForPersistenceAsync(admin);
        Assert.DoesNotContain("ThreePhaseShortCircuit", await viewer.GetStringAsync("/api/events?pageSize=200"));
        Assert.Contains("ThreePhaseShortCircuit", await admin.GetStringAsync("/api/events?pageSize=200"));
    }

    private static async Task<bool> WaitForPersistenceAsync(HttpClient admin)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if ((await admin.GetStringAsync("/api/events?pageSize=200")).Contains("ThreePhaseShortCircuit", StringComparison.Ordinal))
                return true;
            await Task.Delay(100);
        }

        return false;
    }
}
