using Security;
using Xunit;

namespace UnitTests;

public sealed class UserAccessPolicyTests
{
    [Fact]
    public void NamedUserIsAuthenticatedWithAssignedRole()
    {
        var policy = new UserAccessPolicy([
            new UserCredential("ana", AccessRole.Operator, "ana-secret"),
            new UserCredential("marko", AccessRole.Viewer, "marko-secret")
        ]);

        var user = policy.Authenticate("ana-secret");
        Assert.NotNull(user);
        Assert.Equal("ana", user.Username);
        Assert.Equal(AccessRole.Operator, user.Role);
        Assert.Null(policy.Authenticate("wrong"));
    }

    [Fact]
    public void DuplicateUsernamesAndKeysAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new UserAccessPolicy([
            new UserCredential("ana", AccessRole.Viewer, "key-one"),
            new UserCredential("ANA", AccessRole.Operator, "key-two")
        ]));
        Assert.Throws<ArgumentException>(() => new UserAccessPolicy([
            new UserCredential("ana", AccessRole.Viewer, "shared"),
            new UserCredential("marko", AccessRole.Operator, "shared")
        ]));
    }

    [Fact]
    public void LegacyAndNamedCredentialsAuthorizeByRole()
    {
        var legacy = new AccessPolicy(new Dictionary<AccessRole, string>
        {
            [AccessRole.Viewer] = "legacy-viewer"
        });
        var named = new UserAccessPolicy([
            new UserCredential("operator", AccessRole.Operator, "named-operator")
        ]);
        var service = new AuthorizationService(legacy, named);

        Assert.True(service.Authorize("named-operator", AccessRole.Operator));
        Assert.False(service.Authorize("legacy-viewer", AccessRole.Operator));
        Assert.True(service.Authorize("legacy-viewer", AccessRole.Viewer));
        Assert.Equal("operator", service.Authenticate("named-operator")!.Username);
    }
}
