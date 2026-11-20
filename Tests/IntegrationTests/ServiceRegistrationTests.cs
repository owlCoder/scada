using Api.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IntegrationTests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void UnknownStorageProviderIsRejected()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Unknown"
        });

        Assert.Throws<InvalidOperationException>(() => builder.AddScadaServices());
    }

    [Fact]
    public void PostgresRequiresConnectionString()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Postgres",
            ["ConnectionStrings:Scada"] = ""
        });

        Assert.Throws<InvalidOperationException>(() => builder.AddScadaServices());
    }

    [Fact]
    public void InvalidModbusPortIsRejected()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Memory",
            ["Protocols:ModbusEnabled"] = "true",
            ["Protocols:ModbusPort"] = "80"
        });

        Assert.Throws<InvalidOperationException>(() => builder.AddScadaServices());
    }

    [Fact]
    public void NamedUserCannotReuseLegacyApiKey()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Memory",
            ["Security:OperatorApiKey"] = "shared-secret",
            ["Security:Users:0:Username"] = "operator",
            ["Security:Users:0:Role"] = "Operator",
            ["Security:Users:0:ApiKey"] = "shared-secret"
        });

        Assert.Throws<InvalidOperationException>(() => builder.AddScadaServices());
    }

    [Fact]
    public void InvalidNamedUserRoleIsRejected()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Memory",
            ["Security:Users:0:Username"] = "operator",
            ["Security:Users:0:Role"] = "Superuser",
            ["Security:Users:0:ApiKey"] = "secret"
        });

        Assert.Throws<InvalidOperationException>(() => builder.AddScadaServices());
    }

    [Fact]
    public void DuplicateNamedUsernamesAreRejected()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Memory",
            ["Security:Users:0:Username"] = "operator",
            ["Security:Users:0:Role"] = "Operator",
            ["Security:Users:0:ApiKey"] = "first-secret",
            ["Security:Users:1:Username"] = "OPERATOR",
            ["Security:Users:1:Role"] = "Viewer",
            ["Security:Users:1:ApiKey"] = "second-secret"
        });

        Assert.Throws<ArgumentException>(() => builder.AddScadaServices());
    }

    private static WebApplicationBuilder CreateBuilder(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }
}
