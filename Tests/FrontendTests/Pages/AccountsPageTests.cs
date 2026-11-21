using Application.Detection;
using Application.Identity;
using Bunit;
using FrontendTests.Support;
using Hmi.Components;
using Hmi.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Security.Authentication;
using Xunit;

namespace FrontendTests.Pages;

public sealed class AccountsPageTests
{
    [Fact]
    public async Task AccountsRouteShowsAccessDeniedToOperator()
    {
        await using var host = HmiTestHost.Create(AccessRole.Operator);
        host.Context.Services.GetRequiredService<NavigationManager>().NavigateTo("/accounts");

        var route = host.Context.Render<Routes>();

        Assert.Equal("Access denied", route.Find("h1").TextContent);
        Assert.Empty(route.FindComponents<Accounts>());
        Assert.Empty(route.FindAll("a[href=accounts]"));
    }

    [Fact]
    public async Task KeyRotationRequiresConfirmationAndSecretIsOnlyInResultDialog()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator);
        await host.Rig.AccountStore.UpsertAsync(new UserAccount("eva", "Eva", AccessRole.Operator, "hash", null, true,
            host.Rig.Time.GetUtcNow(), null, null, 0, null), CancellationToken.None);
        var page = host.Context.Render<Accounts>();

        Assert.Contains("eva", page.Find("tbody").TextContent);
        Assert.Contains("Detection rule settings", page.Markup);

        page.FindAll("button").Single(button => button.TextContent == "Rotate API key").Click();
        Assert.Contains("Rotate API key", page.Find(".dialog").TextContent);
        Assert.Null((await host.Rig.AccountStore.FindAsync("eva", CancellationToken.None))!.ApiKeyHash);

        page.Find(".dialog .btn-danger").Click();

        var secret = page.Find("input[aria-label='New credential']").GetAttribute("value");
        Assert.False(string.IsNullOrWhiteSpace(secret));
        Assert.Equal(ApiKeyValidator.Fingerprint(secret), (await host.Rig.AccountStore.FindAsync("eva", CancellationToken.None))!.ApiKeyHash);
        Assert.DoesNotContain(secret, string.Join(' ', host.Rig.Buffer.GetRecent().Select(item => item.Message)));

        page.Find(".dialog .btn-primary").Click();
        Assert.Empty(page.FindAll(".dialog"));
        Assert.DoesNotContain(secret, page.Markup);
    }

    [Fact]
    public async Task InvalidRuleSettingsAreRejectedAndValidSettingsArePersistedAfterConfirmation()
    {
        await using var host = HmiTestHost.Create(AccessRole.Administrator);
        var page = host.Context.Render<Accounts>();
        var current = host.Rig.Detection.Rules[0].Settings;
        var store = host.Context.Services.GetRequiredService<IDetectionRuleSettingsStore>();

        page.FindAll("input[type=number]")[1].Change("0");
        page.FindAll("button").Single(button => button.TextContent == "Save rule settings").Click();
        Assert.Equal(current, host.Rig.Detection.Rules[0].Settings);
        page.Find(".dialog .btn-primary").Click();

        Assert.Contains("window must be 1", page.Find(".banner-error").TextContent);
        Assert.Equal(current, host.Rig.Detection.Rules[0].Settings);
        Assert.Empty(await store.LoadAsync(CancellationToken.None));

        page.FindAll("input[type=number]")[1].Change("42");
        page.FindAll("button").Single(button => button.TextContent == "Save rule settings").Click();
        Assert.Empty(await store.LoadAsync(CancellationToken.None));
        page.Find(".dialog .btn-primary").Click();

        page.WaitForAssertion(() => Assert.Contains("Detection rule settings saved", page.Find(".banner-ok").TextContent));
        Assert.Equal(42, host.Rig.Detection.Rules[0].Settings.WindowSeconds);
        Assert.Equal(42, Assert.Single(await store.LoadAsync(CancellationToken.None)).WindowSeconds);
    }
}
