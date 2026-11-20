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

    private static WebApplicationBuilder CreateBuilder(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }
}
