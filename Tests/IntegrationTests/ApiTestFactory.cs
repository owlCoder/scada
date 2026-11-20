using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace IntegrationTests;

public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "Memory",
                ["Protocols:ModbusEnabled"] = "false",
                ["Security:ViewerApiKey"] = "viewer-integration-key",
                ["Security:OperatorApiKey"] = "operator-integration-key",
                ["Security:AdminApiKey"] = "admin-integration-key",
                ["Security:CommandApiKey"] = "",
                ["Security:Users:0:Username"] = "ana",
                ["Security:Users:0:Role"] = "Operator",
                ["Security:Users:0:ApiKey"] = "ana-integration-key"
            });
        });
    }
}
