using Application.Identity;
using DatabaseTests.Support;
using Infrastructure.Persistence.Postgres.Stores;
using Xunit;

namespace DatabaseTests.Stores;

[Collection(PostgresCollection.Name)]
public sealed class UserAccountStoreTests(PostgresContainerFixture postgres)
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly UserAccount Account = new("Ana", "Ana Operator", AccessRole.Operator, "password-hash", "key-hash",
        true, Samples.Start, null, null, 0, null);

    [Fact]
    public async Task ConcurrentFailuresIncrementAtomicallyAndPreventASuccessFromClearingAnActiveLock()
    {
        var store = new PostgresUserAccountStore(await (await postgres.CreateDatabaseAsync()).MigratedAsync());
        Assert.True(await store.TryCreateAsync(Account, None));
        var lockedUntil = Samples.Start.AddMinutes(15);

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            store.RecordFailedSignInAsync(" ANA ", Account.PasswordHash, 5, lockedUntil, Samples.Start, None)));

        var writes = results.OfType<UserAccount>().OrderBy(account => account.Version).ToArray();
        Assert.Equal([1, 2, 3, 4, 0], writes.Select(account => account.FailedLoginCount));
        Assert.Equal(lockedUntil, writes[^1].LockedUntil);
        Assert.Null(await store.RecordSuccessfulSignInAsync("ana", Account.PasswordHash, Samples.Start, None));
        var success = await store.RecordSuccessfulSignInAsync("ana", Account.PasswordHash, lockedUntil, None);
        Assert.NotNull(success);
        Assert.Null(success.LockedUntil);
        Assert.Equal(lockedUntil, success.LastLoginAt);
    }

    [Fact]
    public async Task StaleAdminWritesCannotRestoreRevokedKeysOrEraseSignInBookkeeping()
    {
        var store = new PostgresUserAccountStore(await (await postgres.CreateDatabaseAsync()).MigratedAsync());
        Assert.True(await store.TryCreateAsync(Account, None));
        var stale = (await store.FindAsync("ana", None))!;
        Assert.Equal(stale, await store.FindByApiKeyHashAsync(Account.ApiKeyHash!, None));

        Assert.True(await store.TryUpdateAsync(stale with { ApiKeyHash = null, PasswordHash = "rotated-password-hash" }, None));
        Assert.Null(await store.RecordSuccessfulSignInAsync("ana", stale.PasswordHash, Samples.Start, None));
        var failed = await store.RecordFailedSignInAsync("ana", "rotated-password-hash", 5,
            Samples.Start.AddMinutes(15), Samples.Start, None);
        Assert.NotNull(failed);
        Assert.False(await store.TryUpdateAsync(stale with { Role = AccessRole.Viewer }, None));
        Assert.True(await store.TryUpdateAsync(failed with { Role = AccessRole.Viewer }, None));

        Assert.Null(await store.FindByApiKeyHashAsync(Account.ApiKeyHash!, None));
        Assert.False(await store.TryCreateAsync(Account, None));
        var final = (await store.FindAsync("ana", None))!;
        Assert.Equal(AccessRole.Viewer, final.Role);
        Assert.Equal(1, final.FailedLoginCount);
        Assert.Equal("rotated-password-hash", final.PasswordHash);
        Assert.Null(final.ApiKeyHash);
    }
}
