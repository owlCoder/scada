using Security;
using Xunit;

namespace UnitTests;

public sealed class AccessPolicyTests
{
    private readonly AccessPolicy _policy = new(new Dictionary<AccessRole, string>
    {
        [AccessRole.Viewer] = "viewer-secret",
        [AccessRole.Operator] = "operator-secret",
        [AccessRole.Administrator] = "admin-secret"
    });

    [Fact]
    public void ViewerCannotIssueCommands()
    {
        Assert.True(_policy.Authorize("viewer-secret", AccessRole.Viewer));
        Assert.False(_policy.Authorize("viewer-secret", AccessRole.Operator));
    }

    [Fact]
    public void OperatorCannotManageIncidents()
    {
        Assert.True(_policy.Authorize("operator-secret", AccessRole.Operator));
        Assert.False(_policy.Authorize("operator-secret", AccessRole.Administrator));
    }

    [Fact]
    public void AdministratorHasAllPermissions()
    {
        Assert.True(_policy.Authorize("admin-secret", AccessRole.Administrator));
        Assert.True(_policy.Authorize("admin-secret", AccessRole.Viewer));
    }

    [Fact]
    public void DuplicateKeysAcrossRolesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new AccessPolicy(new Dictionary<AccessRole, string>
        {
            [AccessRole.Viewer] = "shared-secret",
            [AccessRole.Operator] = "shared-secret"
        }));
    }

    [Fact]
    public void UnknownKeyIsRejected()
    {
        Assert.Null(_policy.Authenticate("incorrect"));
        Assert.Null(_policy.Authenticate(null));
    }
}
