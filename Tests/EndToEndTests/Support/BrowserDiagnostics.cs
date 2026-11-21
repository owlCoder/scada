using System.Collections.Concurrent;
using Microsoft.Playwright;
using Xunit;

namespace EndToEndTests.Support;

public sealed class BrowserDiagnostics
{
    private readonly ConcurrentQueue<string> _errors = new();

    public async Task WatchAsync(IPage page)
    {
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                _errors.Enqueue($"{page.Url}: {message.Text}");
        };
        page.PageError += (_, message) => _errors.Enqueue($"{page.Url}: {message}");
        await page.AddInitScriptAsync("""
            globalThis.scadaCspViolations = [];
            document.addEventListener('securitypolicyviolation', event => {
                globalThis.scadaCspViolations.push(`${event.violatedDirective}: ${event.blockedURI}`);
            });
            """);
    }

    public async Task AssertCleanAsync(IPage page)
    {
        // Let the browser dispatch CSP events and finish painting after each navigation.
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        var violations = await page.EvaluateAsync<string[]>("() => globalThis.scadaCspViolations");
        Assert.Empty(violations);
        Assert.Empty(_errors);
    }
}
