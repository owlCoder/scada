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
        await rig.AccountStore.TryCreateAsync(new UserAccount("bob", "Bob", AccessRole.Operator, "hash", null, true,
            rig.Time.GetUtcNow(), null, null, 0, null), CancellationToken.None);
        return rig;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelfLockoutAndRemovingTheLastEnabledAdministratorAreDenied(bool disable)
    {
        var rig = new TestRig();
        var ct = CancellationToken.None;
        await rig.AccountStore.TryCreateAsync(new UserAccount("admin", "Admin", AccessRole.Administrator, "hash", null, true,
            rig.Time.GetUtcNow(), null, null, 0, null), ct);
        var self = new Actor("ADMIN", AccessRole.Administrator);
        var selfResult = disable ? await rig.Accounts.SetEnabledAsync("admin", false, self, ct)
            : await rig.Accounts.ChangeRoleAsync("admin", AccessRole.Operator, self, ct);
        Assert.Equal(AccountOperationStatus.Denied, selfResult.Status);
        Assert.Contains("own account", selfResult.Message);
        var lastResult = disable ? await rig.Accounts.SetEnabledAsync("admin", false, Actor.System("external-admin"), ct)
            : await rig.Accounts.ChangeRoleAsync("admin", AccessRole.Operator, Actor.System("external-admin"), ct);
        Assert.Equal(AccountOperationStatus.Denied, lastResult.Status);
        Assert.Contains("last enabled Administrator", lastResult.Message);
        var account = (await rig.AccountStore.FindAsync("admin", ct))!;
        Assert.True(account.IsEnabled);
        Assert.Equal(AccessRole.Administrator, account.Role);
        await rig.AccountStore.TryCreateAsync(account with { Username = "second", IsEnabled = false }, ct);
        Assert.False((await rig.Accounts.SetEnabledAsync("admin", false, Actor.System("external-admin"), ct)).Succeeded);
        await rig.Accounts.SetEnabledAsync("second", true, Actor.System("external-admin"), ct);
        Assert.False((disable ? await rig.Accounts.SetEnabledAsync("admin", false, self, ct)
            : await rig.Accounts.ChangeRoleAsync("admin", AccessRole.Operator, self, ct)).Succeeded);
        var allowed = disable ? await rig.Accounts.SetEnabledAsync("admin", false, Actor.System("second"), ct)
            : await rig.Accounts.ChangeRoleAsync("admin", AccessRole.Operator, Actor.System("second"), ct);
        Assert.True(allowed.Succeeded);
    }

    [Fact]
    public async Task ConcurrentDemotionsCannotRemoveBothAdministrators()
    {
        var rig = new TestRig();
        var ct = CancellationToken.None;
        foreach (var name in new[] { "a", "b" })
            await rig.AccountStore.TryCreateAsync(new UserAccount(name, name, AccessRole.Administrator, "hash", null, true,
                rig.Time.GetUtcNow(), null, null, 0, null), ct);
        var results = await Task.WhenAll(
            rig.Accounts.ChangeRoleAsync("a", AccessRole.Operator, Actor.System("b"), ct),
            rig.Accounts.SetEnabledAsync("b", false, Actor.System("a"), ct));
        Assert.Single(results, r => r.Succeeded);
        Assert.Single(await rig.AccountStore.ListAsync(ct), a => a.IsEnabled && a.Role == AccessRole.Administrator);
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

    [Fact]
    public async Task RetriedRoleChangePreservesConcurrentKeyRevocationAndLockout()
    {
        var rig = await WithAccountAsync();
        var admin = Actor.System("admin");
        var ct = CancellationToken.None;
        Assert.True((await rig.Accounts.RotateApiKeyAsync("bob", admin, ct)).Succeeded);
        var before = (await rig.AccountStore.FindAsync("bob", ct))!;
        var now = rig.Time.GetUtcNow();
        var lockedUntil = now.AddMinutes(5);
        var store = new ConflictingAccountStore(rig.AccountStore, async () =>
        {
            Assert.True((await rig.Accounts.RevokeApiKeyAsync("bob", admin, ct)).Succeeded);
            Assert.NotNull(await rig.AccountStore.RecordFailedSignInAsync("bob", before.PasswordHash, 1, lockedUntil, now, ct));
        });
        var service = new UserAdministrationService(store, rig.Events, rig.Time);

        Assert.True((await service.ChangeRoleAsync("bob", AccessRole.Viewer, admin, ct)).Succeeded);

        Assert.Equal(2, store.UpdateAttempts);
        var account = (await store.FindAsync("bob", ct))!;
        Assert.Equal(AccessRole.Viewer, account.Role);
        Assert.Null(account.ApiKeyHash);
        Assert.Null(await store.FindByApiKeyHashAsync(before.ApiKeyHash!, ct));
        Assert.Equal(lockedUntil, account.LockedUntil);
        Assert.Single(rig.Buffer.GetRecent(), e => e.Message.StartsWith("Account change: change role of bob", StringComparison.Ordinal));
    }
}
