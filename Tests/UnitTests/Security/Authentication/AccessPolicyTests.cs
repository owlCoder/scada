using Application.Identity;
using Security.Authentication;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Security.Authentication;

public sealed class AccessPolicyTests
{
    private static readonly Dictionary<AccessRole, string> Keys = new()
    {
        [AccessRole.Viewer] = "viewer-secret",
        [AccessRole.Operator] = "operator-secret",
        [AccessRole.Administrator] = "admin-secret"
    };

    [Fact]
    public async Task ViewerCannotIssueCommands()
    {
        var harness = await AuthenticationHarness.CreateAsync(Keys);
        Assert.True(await harness.AuthorizeAsync("viewer-secret", AccessRole.Viewer));
        Assert.False(await harness.AuthorizeAsync("viewer-secret", AccessRole.Operator));
    }

    [Fact]
    public async Task OperatorCannotPerformAdministration()
    {
        var harness = await AuthenticationHarness.CreateAsync(Keys);
        Assert.True(await harness.AuthorizeAsync("operator-secret", AccessRole.Operator));
        Assert.False(await harness.AuthorizeAsync("operator-secret", AccessRole.Administrator));
    }

    [Fact]
    public async Task AdministratorHasAllPermissions()
    {
        var harness = await AuthenticationHarness.CreateAsync(Keys);
        Assert.True(await harness.AuthorizeAsync("admin-secret", AccessRole.Administrator));
        Assert.True(await harness.AuthorizeAsync("admin-secret", AccessRole.Viewer));
    }

    [Fact]
    public Task DuplicateKeysAcrossRolesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new AccessPolicy(new Dictionary<AccessRole, string>
        {
            [AccessRole.Viewer] = "shared-secret",
            [AccessRole.Operator] = "shared-secret"
        }));
        return Task.CompletedTask;
    }

    [Fact]
    public Task InvalidConfiguredRoleIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new AccessPolicy(new Dictionary<AccessRole, string>
        {
            [(AccessRole)99] = "invalid-role-secret"
        }));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task UnknownOrMissingKeyIsRejected()
    {
        var harness = await AuthenticationHarness.CreateAsync(Keys);
        Assert.Null(new AccessPolicy(Keys).Authenticate("incorrect"));
        Assert.Null(new AccessPolicy(Keys).Authenticate(null));
        Assert.Equal(AuthenticationStatus.Missing, (await harness.Authenticator.AuthenticateAsync("", "ip:x", CancellationToken.None)).Status);
        Assert.Equal(AuthenticationStatus.Invalid, (await harness.Authenticator.AuthenticateAsync("nope", "ip:x", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task LegacyKeysGetAStableFingerprintedIdentity()
    {
        var harness = await AuthenticationHarness.CreateAsync(Keys);
        var user = (await harness.AuthenticateAsync("operator-secret"))!;
        Assert.Equal("legacy-Operator", user.Username);
        Assert.Equal(ApiKeyValidator.Fingerprint("operator-secret"), user.CredentialId);
        Assert.DoesNotContain("operator-secret", user.CredentialId);
    }
}
