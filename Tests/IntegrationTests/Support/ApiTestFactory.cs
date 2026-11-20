using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace IntegrationTests.Support;

public static class ApiTestFactory
{
    public static WebApplicationFactory<Program> Create() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
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
            }));
}
