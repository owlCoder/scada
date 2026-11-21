using Application.Events;
using Application.Identity;
using Security.Accounts;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Security.Commands;

public sealed class UserAdministrationServiceTests
{
    private static async Task<TestRig> WithAccountAsync()
    {
        var rig = new TestRig();
        await rig.AccountStore.UpsertAsync(new UserAccount("bob", "Bob", AccessRole.Operator, "hash", null, true,
            rig.Time.GetUtcNow(), null, null, 0, null), CancellationToken.None);
        return rig;
    }

    [Fact]
    public async Task OperatorCannotEscalateAndTheAttemptIsAudited()
    {
        var rig = await WithAccountAsync();

        var result = await rig.Accounts.ChangeRoleAsync("bob", AccessRole.Administrator, new Actor("bob", AccessRole.Operator), CancellationToken.None);

        Assert.Equal(AccountOperationStatus.Denied, result.Status);
        Assert.Equal(AccessRole.Operator, (await rig.AccountStore.FindAsync("bob", CancellationToken.None))!.Role);
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Category == EventCategories.AuthorizationDenied && e.Message.Contains("requires Administrator"));
    }

    [Fact]
    public async Task AdministratorCanChangeRolesAndRotateKeysWithoutLoggingSecrets()
    {
        var rig = await WithAccountAsync();
        var admin = Actor.System("admin");

        Assert.True((await rig.Accounts.ChangeRoleAsync("BOB", AccessRole.Viewer, admin, CancellationToken.None)).Succeeded);
        var rotated = await rig.Accounts.RotateApiKeyAsync("bob", admin, CancellationToken.None);

        Assert.True(rotated.Succeeded);
        Assert.NotNull(rotated.Secret);
        var account = (await rig.AccountStore.FindAsync("bob", CancellationToken.None))!;
        Assert.Equal(AccessRole.Viewer, account.Role);
        Assert.NotNull(account.ApiKeyHash);
        Assert.DoesNotContain(rig.Buffer.GetRecent(), e => e.Message.Contains(rotated.Secret!));
        Assert.Equal(AccountOperationStatus.NotFound, (await rig.Accounts.SetEnabledAsync("nobody", false, admin, CancellationToken.None)).Status);
    }
}
