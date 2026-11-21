using System.Net;
using IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace IntegrationTests.Authentication;

public sealed class DevelopmentAccountTests
{
    [Fact]
    public async Task DevelopmentAccountsAreGeneratedIntoAPrivateFileAndCanSignIn()
    {
        var directory = Directory.CreateTempSubdirectory("scada-dev-");
        var file = Path.Combine(directory.FullName, "dev-credentials.txt");
        await File.WriteAllLinesAsync(file, ["POSTGRES_PASSWORD=keep-me", "SCADA_DEV_ADMINISTRATOR_PASSWORD=stale"]);
        try
        {
            using var factory = ApiTestFactory.Create(("Security:DevelopmentAccounts:Enabled", "true"),
                ("Security:DevelopmentAccounts:CredentialsFile", file)).WithWebHostBuilder(b => b.UseEnvironment("Development"));
            await using var session = BrowserSession.Open(factory);
            _ = factory.Server;

            var values = (await File.ReadAllLinesAsync(file)).Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
            Assert.Equal("keep-me", values["POSTGRES_PASSWORD"]);
            Assert.NotEqual("stale", values["SCADA_DEV_ADMINISTRATOR_PASSWORD"]);
            Assert.Equal("dev-operator", values["SCADA_DEV_OPERATOR_USERNAME"]);
            Assert.Equal(64, values["SCADA_DEV_VIEWER_API_KEY"].Length);
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));

            var response = await session.SignInAsync("dev-admin", values["SCADA_DEV_ADMINISTRATOR_PASSWORD"]);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            using var keyClient = factory.Client(values["SCADA_DEV_OPERATOR_API_KEY"]);
            Assert.Equal(HttpStatusCode.OK, (await keyClient.GetAsync("/api/me")).StatusCode);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task DevelopmentAccountsAreNeverCreatedOutsideDevelopment()
    {
        var file = Path.Combine(Path.GetTempPath(), $"scada-dev-{Guid.NewGuid():N}.txt");
        using var factory = ApiTestFactory.Create(("Security:DevelopmentAccounts:Enabled", "true"),
            ("Security:DevelopmentAccounts:CredentialsFile", file)).WithWebHostBuilder(b => b.UseEnvironment("Production"));
        using var admin = factory.Client(ApiTestFactory.AdminKey);

        var accounts = await admin.GetJsonAsync("/api/accounts");

        Assert.DoesNotContain(accounts.EnumerateArray(), a => a.GetProperty("username").GetString()!.StartsWith("dev-", StringComparison.Ordinal));
        Assert.False(File.Exists(file));
    }
}
