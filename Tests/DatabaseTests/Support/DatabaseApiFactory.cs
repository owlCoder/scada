using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DatabaseTests.Support;

/// <summary>Runs the real API host with <c>Storage:Provider=Postgres</c> against a test database.</summary>
public static class DatabaseApiFactory
{
    public const string OperatorKey = "operator-database-key";
    public const string AdminKey = "admin-database-key";

    public static WebApplicationFactory<Program> Create(TestDatabase database, params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Storage:Provider", "Postgres");
            builder.UseSetting("ConnectionStrings:Scada", database.ConnectionString);
            builder.UseSetting("Security:ViewerApiKey", "viewer-database-key");
            builder.UseSetting("Security:OperatorApiKey", OperatorKey);
            builder.UseSetting("Security:AdminApiKey", AdminKey);
            builder.UseSetting("Security:DevelopmentAccounts:Enabled", "false");
            builder.UseSetting("Security:Users:0:Username", "ana");
            builder.UseSetting("Security:Users:0:Role", "Operator");
            builder.UseSetting("Security:Users:0:ApiKey", "ana-database-key");
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        });

    public static HttpClient Client(this WebApplicationFactory<Program> factory, string key)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

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
