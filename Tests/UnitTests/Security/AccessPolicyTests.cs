using Security;
using Xunit;

namespace UnitTests.Security;

public sealed class AccessPolicyTests
{
    private readonly AccessPolicy _policy = new(new Dictionary<AccessRole, string>
    {
        [AccessRole.Viewer] = "viewer-secret",
        [AccessRole.Operator] = "operator-secret",
        [AccessRole.Administrator] = "admin-secret"
    });

    [Fact]
    public Task ViewerCannotIssueCommands()
    {
        Assert.True(_policy.Authorize("viewer-secret", AccessRole.Viewer));
        Assert.False(_policy.Authorize("viewer-secret", AccessRole.Operator));
        return Task.CompletedTask;
    }

    [Fact]
    public Task OperatorCannotManageIncidents()
    {
        Assert.True(_policy.Authorize("operator-secret", AccessRole.Operator));
        Assert.False(_policy.Authorize("operator-secret", AccessRole.Administrator));
        return Task.CompletedTask;
    }

    [Fact]
    public Task AdministratorHasAllPermissions()
    {
        Assert.True(_policy.Authorize("admin-secret", AccessRole.Administrator));
        Assert.True(_policy.Authorize("admin-secret", AccessRole.Viewer));
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
    public Task InvalidRequiredRoleIsRejected()
    {
        Assert.False(_policy.Authorize("admin-secret", (AccessRole)(-1)));
        return Task.CompletedTask;
    }

    [Fact]
    public Task UnknownKeyIsRejected()
    {
        Assert.Null(_policy.Authenticate("incorrect"));
        Assert.Null(_policy.Authenticate(null));
        return Task.CompletedTask;
    }
}
