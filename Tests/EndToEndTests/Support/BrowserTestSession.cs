using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;
using Xunit;

namespace EndToEndTests.Support;

/// <summary>A fresh real API and isolated cookie contexts for each browser test.</summary>
public sealed class BrowserTestSession(WebApplicationFactory<Program> factory, IBrowser browser) : IAsyncDisposable
{
    public const string Password = "browser-only-test-password";
    public const string InstallCommand = "node Tests/EndToEndTests/bin/Release/net10.0/.playwright/package/cli.js install chromium";
    private readonly List<IBrowserContext> _contexts = [];

    public WebApplicationFactory<Program> Factory { get; } = factory;
    public Uri Address { get; private set; } = null!;

    public static async Task RunAsync(Func<BrowserTestSession, Task> test, ITestOutputHelper output,
        [CallerMemberName] string name = "browser-test")
    {
        using var playwright = await Playwright.CreateAsync();
        IBrowser browser;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        }
        catch (PlaywrightException exception) when (exception.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            var message = $"Chromium is not installed. Run: {InstallCommand}. Set SCADA_E2E_REQUIRED=1 to require browser tests.";
            if (Environment.GetEnvironmentVariable("SCADA_E2E_REQUIRED") == "1")
                Assert.Fail(message);
            Assert.Skip(message);
            throw;
        }

        await using var ownedBrowser = browser;
        await using var session = new BrowserTestSession(CreateFactory(), browser);
        try
        {
            // .NET 10's Kestrel integration starts a real socket server, without a free-port race.
            session.Factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            session.Factory.StartServer();
            using var client = session.Factory.CreateClient();
            session.Address = client.BaseAddress!;
            await test(session);
        }
        catch
        {
            await session.SaveFailureAsync(name, output);
            throw;
        }
    }

    public async Task<IPage> NewPageAsync()
    {
        var context = await browser.NewContextAsync(new() { BaseURL = Address.ToString(), ViewportSize = new() { Width = 1440, Height = 1000 } });
        _contexts.Add(context);
        context.SetDefaultTimeout(7_000);
        context.SetDefaultNavigationTimeout(10_000);
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        return await context.NewPageAsync();
    }

    public static async Task LoginAsync(IPage page, string username)
    {
        await page.GotoAsync("/account/login");
        await page.GetByLabel("Username").FillAsync(username);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Overview", Exact = true })).ToBeVisibleAsync();
    }

    public static async Task NavigateAsync(IPage page, string label)
    {
        await page.GetByRole(AriaRole.Navigation).GetByRole(AriaRole.Link, new() { Name = label, Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync(label);
    }

    public static async Task ConfirmAsync(IPage page)
    {
        var dialog = page.GetByRole(AriaRole.Alertdialog);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Confirm", Exact = true }).ClickAsync();
        await Assertions.Expect(dialog).ToHaveCountAsync(0);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts)
            await context.CloseAsync();
        await Factory.DisposeAsync();
    }

    private async Task SaveFailureAsync(string name, ITestOutputHelper output)
    {
        var directory = Path.Combine(RepositoryRoot(), "TestResults", "EndToEndTests",
            $"{name}-{TimeProvider.System.GetUtcNow():yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        output.WriteLine($"Browser failure artifacts: {directory}");
        for (var index = 0; index < _contexts.Count; index++)
        {
            var context = _contexts[index];
            for (var pageIndex = 0; pageIndex < context.Pages.Count; pageIndex++)
            {
                try
                {
                    await context.Pages[pageIndex].ScreenshotAsync(new()
                    {
                        Path = Path.Combine(directory, $"context-{index}-page-{pageIndex}.png"),
                        FullPage = true,
                        Timeout = 3_000
                    });
                }
                catch (PlaywrightException exception)
                {
                    output.WriteLine($"Could not capture page {pageIndex}: {exception.Message}");
                }
            }
            try
            {
                await context.Tracing.StopAsync(new() { Path = Path.Combine(directory, $"context-{index}-trace.zip") });
            }
            catch (PlaywrightException exception)
            {
                output.WriteLine($"Could not capture context {index}: {exception.Message}");
            }
        }
    }

    private static WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Storage:Provider", "Memory");
        builder.UseSetting("Security:DevelopmentAccounts:Enabled", "false");
        builder.UseSetting("Security:ApiKeys:Enabled", "false");
        builder.UseSetting("Security:ViewerApiKey", "");
        builder.UseSetting("Security:OperatorApiKey", "");
        builder.UseSetting("Security:AdminApiKey", "");
        builder.UseSetting("Security:Cookies:RequireSecure", "false");
        builder.UseSetting("Authentication:Oidc:Enabled", "false");
        builder.UseSetting("Security:Session:RevalidationInterval", "00:00:02");
        builder.UseSetting("Protocols:Modbus:Enabled", "false");
        builder.UseSetting("Protocols:OpcUa:Enabled", "false");
        builder.UseSetting("Simulation:Host:RunLoop", "true");
        builder.UseSetting("Simulation:TimeScale", "10");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        var roles = new[] { "Viewer", "Operator", "Administrator" };
        for (var index = 0; index < roles.Length; index++)
        {
            builder.UseSetting($"Security:Users:{index}:Username", roles[index].ToLowerInvariant());
            builder.UseSetting($"Security:Users:{index}:Role", roles[index]);
            builder.UseSetting($"Security:Users:{index}:Password", Password);
            builder.UseSetting($"Security:Users:{index}:PasswordHash", "");
            builder.UseSetting($"Security:Users:{index}:ApiKey", "");
        }
    });

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "scada.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate scada.slnx.");
    }
}
