using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Security;

namespace IntegrationTests.Support;

public static class ApiTestFactory
{
    public static WebApplicationFactory<Program> Create() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<AccessPolicy>();
                services.RemoveAll<UserAccessPolicy>();

                services.AddSingleton(new AccessPolicy(new Dictionary<AccessRole, string>
                {
                    [AccessRole.Viewer] = "viewer-integration-key",
                    [AccessRole.Operator] = "operator-integration-key",
                    [AccessRole.Administrator] = "admin-integration-key"
                }));

                services.AddSingleton(new UserAccessPolicy(
                [
                    new UserCredential("ana", AccessRole.Operator, "ana-integration-key")
                ]));
            }));
}
