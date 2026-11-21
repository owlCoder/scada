using Api.Configuration;
using Application.Persistence;
using Infrastructure.Persistence.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntegrationTests.Configuration;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public Task UnknownStorageProviderIsRejected()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Unknown"
        });

        Assert.Throws<InvalidOperationException>(() => builder.AddScadaServices());
        return Task.CompletedTask;
    }

    [Fact]
    public Task PostgresRequiresConnectionString()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Postgres",
            ["ConnectionStrings:Scada"] = ""
        });

        Assert.Throws<InvalidOperationException>(() => builder.AddScadaServices());
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("Memory")]
    [InlineData("InMemory")]
    [InlineData("inmemory")]
    public Task MemoryProviderNamesRegisterInMemoryStorage(string provider)
    {
        var builder = CreateBuilder(new Dictionary<string, string?> { ["Storage:Provider"] = provider });
        builder.AddScadaServices();
        var registration = Assert.Single(builder.Services, service => service.ServiceType == typeof(IStorageProbe));
        Assert.Equal(typeof(InMemoryStorageProbe), registration.ImplementationType);
        return Task.CompletedTask;
    }

    [Fact]
    public Task InvalidModbusPortIsRejected()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Memory",
            ["Protocols:ModbusEnabled"] = "true",
            ["Protocols:ModbusPort"] = "80"
        });

        Assert.Throws<InvalidOperationException>(() => builder.AddScadaServices());
        return Task.CompletedTask;
    }

    [Fact]
    public Task NamedUserCannotReuseLegacyApiKey()
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
        return Task.CompletedTask;
    }

    [Fact]
    public Task InvalidNamedUserRoleIsRejected()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "Memory",
            ["Security:Users:0:Username"] = "operator",
            ["Security:Users:0:Role"] = "Superuser",
            ["Security:Users:0:ApiKey"] = "secret"
        });

        Assert.Throws<InvalidOperationException>(() => builder.AddScadaServices());
        return Task.CompletedTask;
    }

    [Fact]
    public Task DuplicateNamedUsernamesAreRejected()
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
        return Task.CompletedTask;
    }

    private static WebApplicationBuilder CreateBuilder(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }
}
