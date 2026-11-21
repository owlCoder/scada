using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IntegrationTests.Support;

public static class ApiTestFactory
{
    public const string ViewerKey = "viewer-integration-key";
    public const string OperatorKey = "operator-integration-key";
    public const string AdminKey = "admin-integration-key";
    public const string AnaKey = "ana-integration-key";

    /// <summary>Builds the API with test credentials. Extra settings override the defaults.</summary>
    public static WebApplicationFactory<Program> Create(params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Security:ViewerApiKey", ViewerKey);
            builder.UseSetting("Security:OperatorApiKey", OperatorKey);
            builder.UseSetting("Security:AdminApiKey", AdminKey);
            builder.UseSetting("Security:DevelopmentAccounts:Enabled", "false");
            builder.UseSetting("Security:Users:0:Username", "ana");
            builder.UseSetting("Security:Users:0:Role", "Operator");
            builder.UseSetting("Security:Users:0:ApiKey", AnaKey);
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        });

    public static HttpClient Client(this WebApplicationFactory<Program> factory, string? key)
    {
        var client = factory.CreateClient();
        if (key is not null)
            client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string path) =>
        await client.GetFromJsonAsync<JsonElement>(path);

    /// <summary>Polls until <paramref name="condition"/> holds; persistence and the simulation loop are asynchronous.</summary>
    public static async Task<T> EventuallyAsync<T>(Func<Task<T>> probe, Func<T, bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (true)
        {
            var value = await probe();
            if (condition(value) || DateTime.UtcNow > deadline)
                return value;
            await Task.Delay(100);
        }
    }
}
