using System.Net;
using System.Security.Claims;
using Api.Authentication;
using Application.Identity;
using IntegrationTests.Support;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace IntegrationTests.Authentication;

public sealed class OidcSignInTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public Task StartupWarnsOnlyWhenEnabledOidcAllowsHttpMetadata(bool enabled, bool requireHttps, bool expected)
    {
        var collector = new FakeLogCollector();
        using var logging = new LoggerFactory([new FakeLoggerProvider(collector)]);
        using var baseline = ApiTestFactory.Create(
            ("Authentication:Oidc:Enabled", enabled.ToString()),
            ("Authentication:Oidc:Authority", "https://identity.example"),
            ("Authentication:Oidc:ClientId", "scada-api"),
            ("Authentication:Oidc:RequireHttpsMetadata", requireHttps.ToString()));
        using var factory = baseline.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerFactory>(logging)));
        using var client = factory.CreateClient();

        var warnings = collector.GetSnapshot().Where(record => record.Level == LogLevel.Warning
            && record.Message.Contains("RequireHttpsMetadata=false", StringComparison.Ordinal));
        Assert.Equal(expected ? 1 : 0, warnings.Count());
        return Task.CompletedTask;
    }

    private const string Authority = "https://idp.example.test/realms/scada";

    [Fact]
    public async Task SsoChallengeRedirectsToTheProviderWithPkce()
    {
        using var factory = ApiTestFactory.Create(("Authentication:Oidc:Enabled", "true"), ("Authentication:Oidc:Authority", Authority),
            ("Authentication:Oidc:ClientId", "scada-api")).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                // Static provider metadata, so the test needs no network access to an identity provider.
                services.PostConfigure<OpenIdConnectOptions>(ScadaAuthenticationSchemes.Oidc, options => options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(new OpenIdConnectConfiguration
                    {
                        Issuer = Authority, AuthorizationEndpoint = $"{Authority}/protocol/openid-connect/auth"
                    }))));
        await using var session = BrowserSession.Open(factory);

        Assert.Contains("/account/login/oidc", await session.Client.GetStringAsync("/account/login"));
        var response = await session.Client.GetAsync("/account/login/oidc?returnUrl=/alarms");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith($"{Authority}/protocol/openid-connect/auth", location);
        Assert.Contains("client_id=scada-api", location);
        Assert.Contains("code_challenge_method=S256", location);
        Assert.Contains("response_type=code", location);
    }

    [Fact]
    public async Task SsoIsUnavailableUnlessConfigured()
    {
        using var factory = ApiTestFactory.Create();
        await using var session = BrowserSession.Open(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await session.Client.GetAsync("/account/login/oidc")).StatusCode);
        Assert.DoesNotContain("/account/login/oidc", await session.Client.GetStringAsync("/account/login"));
    }

    [Fact]
    public Task EnabledOidcWithoutAuthorityFailsAtStartup()
    {
        using var factory = ApiTestFactory.Create(("Authentication:Oidc:Enabled", "true"));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Authentication:Oidc:Authority", error.Message);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(new[] { "scada-viewer", "scada-administrator" }, AccessRole.Administrator)]
    [InlineData(new[] { "scada-operator" }, AccessRole.Operator)]
    [InlineData(new[] { "scada-viewer", "offline_access" }, AccessRole.Viewer)]
    public Task ProviderRolesMapToTheHighestScadaRole(string[] roles, AccessRole expected)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "1234"), new Claim("preferred_username", "marija"), .. roles.Select(r => new Claim("roles", r))], "oidc"));

        var user = OidcRoleMapper.Map(principal, new OidcSettings());

        Assert.Equal(expected, user!.Role);
        Assert.Equal("marija", user.Username);
        Assert.Equal("oidc:1234", user.CredentialId);
        return Task.CompletedTask;
    }

    [Fact]
    public Task IdentitiesWithoutAScadaRoleAreRejected()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "1"), new Claim("roles", "offline_access")], "oidc"));

        Assert.Null(OidcRoleMapper.Map(principal, new OidcSettings()));
        return Task.CompletedTask;
    }
}
