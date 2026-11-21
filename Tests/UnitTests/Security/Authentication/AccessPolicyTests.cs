using Application.Identity;
using Security.Authentication;
using Xunit;

namespace UnitTests.Security.Authentication;

public sealed class AccessPolicyTests
{
    private readonly AccessPolicy _policy = new(new Dictionary<AccessRole, string>
    {
        [AccessRole.Viewer] = "viewer-secret",
        [AccessRole.Operator] = "operator-secret",
        [AccessRole.Administrator] = "admin-secret"
    });

    private ApiKeyAuthenticator Authenticator => new(_policy, new UserAccessPolicy([]));

    [Fact]
    public Task ViewerCannotIssueCommands()
    {
        Assert.True(Authenticator.Authorize("viewer-secret", AccessRole.Viewer));
        Assert.False(Authenticator.Authorize("viewer-secret", AccessRole.Operator));
        return Task.CompletedTask;
    }

    [Fact]
    public Task OperatorCannotPerformAdministration()
    {
        Assert.True(Authenticator.Authorize("operator-secret", AccessRole.Operator));
        Assert.False(Authenticator.Authorize("operator-secret", AccessRole.Administrator));
        return Task.CompletedTask;
    }

    [Fact]
    public Task AdministratorHasAllPermissions()
    {
        Assert.True(Authenticator.Authorize("admin-secret", AccessRole.Administrator));
        Assert.True(Authenticator.Authorize("admin-secret", AccessRole.Viewer));
        return Task.CompletedTask;
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
    public Task UnknownKeyIsRejected()
    {
        Assert.Null(_policy.Authenticate("incorrect"));
        Assert.Null(_policy.Authenticate(null));
        Assert.Null(Authenticator.Authenticate(""));
        return Task.CompletedTask;
    }

    [Fact]
    public Task LegacyKeysGetAStableFingerprintedIdentity()
    {
        var user = Authenticator.Authenticate("operator-secret")!;
        Assert.Equal("legacy-Operator", user.Username);
        Assert.Equal(ApiKeyValidator.Fingerprint("operator-secret"), user.CredentialId);
        Assert.DoesNotContain("operator-secret", user.CredentialId);
        return Task.CompletedTask;
    }
}
