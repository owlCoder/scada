using Application.Events;
using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components.Pages;
using Xunit;

namespace FrontendTests.Pages;

public sealed class EventsPageTests
{
    [Fact]
    public async Task SearchInputsHaveAMaximumLength()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var page = host.Context.Render<Events>();
        Assert.All(page.FindAll("input[type=search]"), input =>
            Assert.Equal("200", input.GetAttribute("maxlength")));
    }

    [Fact]
    public async Task ListShowsCorrelationAndFiltersByCategoryActorAndMessage()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var now = host.Rig.Time.GetUtcNow();
        await host.Rig.EventStore.AppendAsync(
        [
            SecurityEvent.Create(now, EventCategories.Audit, EventSeverity.Information, "tests", "Opened outlet", "eva")
                with { CorrelationId = "trace-opening" },
            SecurityEvent.Create(now, EventCategories.Network, EventSeverity.Medium, "tests", "Opened link", "eva"),
            SecurityEvent.Create(now, EventCategories.Audit, EventSeverity.Information, "tests", "Opened inlet", "ana"),
            SecurityEvent.Create(now, EventCategories.Audit, EventSeverity.Information, "tests", "Closed outlet", "eva")
        ], CancellationToken.None);

        var page = host.Context.Render<Events>();
        Assert.Equal(4, page.FindAll("tbody tr").Count);
        Assert.Contains("trace-opening", page.Find("tbody").TextContent);

        page.FindAll("input")[0].Change("Audit");
        page.FindAll("input")[1].Change("eva");
        page.FindAll("input")[2].Change("Opened");
        page.Find("form").Submit();

        Assert.Contains("Opened outlet", Assert.Single(page.FindAll("tbody tr")).TextContent);
        Assert.Empty(page.FindAll(".dialog"));
    }

    [Fact]
    public async Task EventsRefreshFirstPageAfterPersistenceAndLeaveOlderPageInPlace()
    {
        await using var host = HmiTestHost.Create(AccessRole.Viewer);
        var now = host.Rig.Time.GetUtcNow();
        await host.Rig.EventStore.AppendAsync(Enumerable.Range(1, 51)
            .Select(index => SecurityEvent.Create(now.AddSeconds(index - 60), EventCategories.Audit,
                EventSeverity.Information, "tests", $"Initial {index}", "eva")).ToArray(), CancellationToken.None);
        var page = host.Context.Render<Events>();

        host.Rig.Events.Publish(SecurityEvent.Create(now, EventCategories.Audit, EventSeverity.Information, "tests", "Latest event", "eva"));
        await host.Rig.Worker.DrainAsync(CancellationToken.None);
        page.WaitForAssertion(() => Assert.Contains("Latest event", page.Find("tbody tr:first-child").TextContent));

        page.FindAll(".pager button")[1].Click();
        Assert.Contains("Page 2 of 2", page.Find(".pager").TextContent);
        var historicRows = page.Find("tbody").TextContent;

        host.Rig.Events.Publish(SecurityEvent.Create(now.AddSeconds(1), EventCategories.Audit,
            EventSeverity.Information, "tests", "Another event", "eva"));
        await host.Rig.Worker.DrainAsync(CancellationToken.None);

        Assert.Equal(historicRows, page.Find("tbody").TextContent);
        page.FindAll(".pager button")[0].Click();
        page.WaitForAssertion(() => Assert.Contains("Another event", page.Find("tbody tr:first-child").TextContent));
    }
}
