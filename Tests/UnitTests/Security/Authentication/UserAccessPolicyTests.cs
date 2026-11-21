using Application.Events;
using Application.Identity;
using Security.Authentication;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Security.Authentication;

public sealed class UserAccessPolicyTests
{
    private static readonly Dictionary<AccessRole, string> Legacy = new() { [AccessRole.Viewer] = "legacy-viewer" };

    [Fact]
    public async Task NamedUserIsAuthenticatedWithAssignedRole()
    {
        var harness = await AuthenticationHarness.CreateAsync(Legacy,
            new UserCredential("ana", AccessRole.Operator, "ana-secret"), new UserCredential("marko", AccessRole.Viewer, "marko-secret"));

        var user = await harness.AuthenticateAsync("ana-secret");
        Assert.Equal("ana", user!.Username);
        Assert.Equal(AccessRole.Operator, user.Role);
        Assert.Null(await harness.AuthenticateAsync("wrong"));
    }

    [Fact]
    public Task DuplicateUsernamesAndKeysAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new UserAccessPolicy([
            new UserCredential("ana", AccessRole.Viewer, "key-one"),
            new UserCredential("ANA", AccessRole.Operator, "key-two")
        ]));
        Assert.Throws<ArgumentException>(() => new UserAccessPolicy([
            new UserCredential("ana", AccessRole.Viewer, "shared"),
            new UserCredential("marko", AccessRole.Operator, "shared")
        ]));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task LegacyAndNamedCredentialsAuthorizeByRole()
    {
        var harness = await AuthenticationHarness.CreateAsync(Legacy, new UserCredential("operator", AccessRole.Operator, "named-operator"));

        Assert.True(await harness.AuthorizeAsync("named-operator", AccessRole.Operator));
        Assert.False(await harness.AuthorizeAsync("legacy-viewer", AccessRole.Operator));
        Assert.True(await harness.AuthorizeAsync("legacy-viewer", AccessRole.Viewer));
        Assert.Equal("operator", (await harness.AuthenticateAsync("named-operator"))!.Username);
    }

    [Fact]
    public async Task AccountChangesTakeEffectImmediately()
    {
        var harness = await AuthenticationHarness.CreateAsync(Legacy, new UserCredential("ana", AccessRole.Operator, "ana-secret"));
        var admin = Actor.System("admin");

        await harness.Accounts.ChangeRoleAsync("ana", AccessRole.Viewer, admin, CancellationToken.None);
        Assert.Equal(AccessRole.Viewer, (await harness.AuthenticateAsync("ana-secret"))!.Role);

        var rotated = await harness.Accounts.RotateApiKeyAsync("ana", admin, CancellationToken.None);
        Assert.Null(await harness.AuthenticateAsync("ana-secret"));
        Assert.Equal("ana", (await harness.AuthenticateAsync(rotated.Secret))!.Username);

        await harness.Accounts.SetEnabledAsync("ana", false, admin, CancellationToken.None);
        Assert.Equal(AuthenticationStatus.Disabled,
            (await harness.Authenticator.AuthenticateAsync(rotated.Secret, AuthenticationHarness.Source, CancellationToken.None)).Status);

        await harness.Accounts.RevokeApiKeyAsync("ana", admin, CancellationToken.None);
        await harness.Accounts.SetEnabledAsync("ana", true, admin, CancellationToken.None);
        Assert.Null(await harness.AuthenticateAsync(rotated.Secret));
    }

    [Fact]
    public async Task FailedAttemptsAreAuditedAndThrottledPerSource()
    {
        var harness = await AuthenticationHarness.CreateAsync(Legacy);

        for (var i = 0; i < 10; i++)
            Assert.Null(await harness.AuthenticateAsync($"guess-{i}"));

        var blocked = await harness.Authenticator.AuthenticateAsync("legacy-viewer", AuthenticationHarness.Source, CancellationToken.None);
        var other = await harness.Authenticator.AuthenticateAsync("legacy-viewer", "ip:203.0.113.1", CancellationToken.None);
        Assert.Equal(AuthenticationStatus.Throttled, blocked.Status);
        Assert.True(other.Succeeded);
        Assert.Equal(10, harness.Events.GetRecent().Count(e => e.Category == EventCategories.Authentication && e.Actor == AuthenticationHarness.Source));

        harness.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await harness.Authenticator.AuthenticateAsync("legacy-viewer", AuthenticationHarness.Source, CancellationToken.None)).Succeeded);
    }
}
