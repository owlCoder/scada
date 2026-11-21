using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IntegrationTests.Support;

/// <summary>A cookie-carrying client that does not follow redirects, for testing the browser sign-in flow.</summary>
public sealed partial class BrowserSession(HttpClient client) : IAsyncDisposable
{
    public const string EvaPassword = "eva-integration-password";

    public HttpClient Client { get; } = client;

    public static BrowserSession Open(WebApplicationFactory<Program> factory) =>
        new(factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true }));

    /// <summary>Settings that add the password user eva (Operator) to <see cref="ApiTestFactory.Create"/>.</summary>
    public static (string, string)[] EvaSettings(params (string, string)[] extra) =>
    [
        ("Security:Users:1:Username", "eva"), ("Security:Users:1:Role", "Operator"), ("Security:Users:1:Password", EvaPassword),
        .. extra
    ];

    public async Task<string> FormTokenAsync()
    {
        var page = await Client.GetStringAsync("/account/login");
        return FormToken().Match(page).Groups[1].Value;
    }

    public async Task<HttpResponseMessage> SignInAsync(string username, string password, string returnUrl = "/", string? token = null)
    {
        token ??= await FormTokenAsync();
        return await Client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["username"] = username, ["password"] = password, ["returnUrl"] = returnUrl
        }));
    }

    /// <summary>Request token for the X-CSRF-TOKEN header on state-changing API calls.</summary>
    public async Task<string> ApiTokenAsync() =>
        (await Client.GetFromJsonAsync<JsonElement>("/account/antiforgery")).GetProperty("token").GetString()!;

    public async Task<HttpStatusCode> PostApiAsync(string path, string? csrfToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (csrfToken is not null)
            request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return (await Client.SendAsync(request)).StatusCode;
    }

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        return ValueTask.CompletedTask;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex FormToken();
}
