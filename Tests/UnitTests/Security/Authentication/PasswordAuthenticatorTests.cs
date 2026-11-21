using Application.Identity;
using Security.Accounts;
using Security.Authentication;
using UnitTests.Support;
using Xunit;

namespace UnitTests.Security.Authentication;

public sealed class PasswordAuthenticatorTests
{
    private const string Password = "correct-horse-battery";
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task CorrectPasswordSignsInAndResetsTheFailureCount()
    {
        var (rig, authenticator) = await CreateAsync();
        await authenticator.AuthenticateAsync("eva", "wrong-password-1", "ip:a", None);

        var result = await authenticator.AuthenticateAsync(" EVA ", Password, "ip:a", None);

        Assert.Equal(AuthenticationStatus.Succeeded, result.Status);
        Assert.Equal("eva", result.User!.Username);
        Assert.Equal(AccessRole.Operator, result.User.Role);
        var account = await rig.AccountStore.FindAsync("eva", None);
        Assert.Equal(0, account!.FailedLoginCount);
        Assert.Equal(rig.Time.GetUtcNow(), account.LastLoginAt);
    }

    [Fact]
    public async Task RepeatedFailuresLockTheAccountUntilTheLockoutExpires()
    {
        var (rig, authenticator) = await CreateAsync(new PasswordLockoutOptions { MaxFailedAttempts = 3, LockoutDuration = TimeSpan.FromMinutes(10) });
        for (var i = 0; i < 2; i++)
            Assert.Equal(AuthenticationStatus.Invalid, (await authenticator.AuthenticateAsync("eva", "nope-nope-nope", $"ip:{i}", None)).Status);

        Assert.Equal(AuthenticationStatus.Locked, (await authenticator.AuthenticateAsync("eva", "nope-nope-nope", "ip:9", None)).Status);
        Assert.Equal(AuthenticationStatus.Locked, (await authenticator.AuthenticateAsync("eva", Password, "ip:9", None)).Status);

        rig.Time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(AuthenticationStatus.Succeeded, (await authenticator.AuthenticateAsync("eva", Password, "ip:9", None)).Status);
        Assert.Contains(rig.Buffer.GetRecent(), e => e.Message.Contains("locked after 3 failed sign-ins"));
    }

    [Fact]
    public async Task UnknownAndDisabledUsersFailAndAreAuditedWithoutThePassword()
    {
        var (rig, authenticator) = await CreateAsync();
        await rig.Accounts.SetEnabledAsync("eva", false, new Actor("admin", AccessRole.Administrator), None);

        Assert.Equal(AuthenticationStatus.Invalid, (await authenticator.AuthenticateAsync("ghost", "secret-guess-1", "ip:a", None)).Status);
        Assert.Equal(AuthenticationStatus.Disabled, (await authenticator.AuthenticateAsync("eva", Password, "ip:a", None)).Status);
        Assert.Equal(AuthenticationStatus.Missing, (await authenticator.AuthenticateAsync("eva", "", "ip:a", None)).Status);

        var messages = rig.Buffer.GetRecent().Select(e => e.Message).ToArray();
        Assert.Contains(messages, m => m.Contains("unknown user ghost"));
        Assert.Contains(messages, m => m.Contains("disabled account eva"));
        Assert.DoesNotContain(messages, m => m.Contains("secret-guess-1") || m.Contains(Password));
    }

    [Fact]
    public async Task ConcurrentFailuresLockTheAccountWithoutLosingIncrements()
    {
        var (rig, authenticator) = await CreateAsync(new PasswordLockoutOptions { MaxFailedAttempts = 5 });
        var attempts = Enumerable.Range(0, 12).Select(i => Task.Run(() =>
            authenticator.AuthenticateAsync("Eva", "wrong-password", $"ip:{i}", None))).ToArray();

        var results = await Task.WhenAll(attempts);

        Assert.Equal(4, results.Count(r => r.Status == AuthenticationStatus.Invalid));
        Assert.Equal(8, results.Count(r => r.Status == AuthenticationStatus.Locked));
        var account = (await rig.AccountStore.FindAsync("eva", None))!;
        Assert.Equal(0, account.FailedLoginCount);
        Assert.Equal(5, account.Version);
        Assert.True(account.LockedUntil > rig.Time.GetUtcNow());
        Assert.Single(rig.Buffer.GetRecent(), e => e.Message.Contains("locked after 5 failed sign-ins"));
        Assert.Equal(AuthenticationStatus.Locked, (await authenticator.AuthenticateAsync("eva", Password, "ip:correct", None)).Status);
    }

    [Fact]
    public async Task ManyFailuresFromOneSourceAreThrottled()
    {
        var (_, authenticator) = await CreateAsync(new PasswordLockoutOptions { MaxFailedAttempts = 100 });
        for (var i = 0; i < 10; i++)
            await authenticator.AuthenticateAsync($"user{i}", "wrong-password-1", "ip:flood", None);

        Assert.Equal(AuthenticationStatus.Throttled, (await authenticator.AuthenticateAsync("eva", Password, "ip:flood", None)).Status);
        Assert.Equal(AuthenticationStatus.Succeeded, (await authenticator.AuthenticateAsync("eva", Password, "ip:other", None)).Status);
    }

    [Fact]
    public async Task SessionsEndWhenThePasswordChangesAndFollowRoleChanges()
    {
        var (rig, authenticator) = await CreateAsync();
        var admin = new Actor("admin", AccessRole.Administrator);
        var issued = rig.Time.GetUtcNow();

        Assert.Equal(AccessRole.Operator, (await authenticator.ValidateSessionAsync("eva", issued, None))!.Role);
        await rig.Accounts.ChangeRoleAsync("eva", AccessRole.Viewer, admin, None);
        Assert.Equal(AccessRole.Viewer, (await authenticator.ValidateSessionAsync("eva", issued, None))!.Role);

        rig.Time.Advance(TimeSpan.FromMinutes(1));
        await rig.Accounts.RotatePasswordAsync("eva", admin, None);
        Assert.Null(await authenticator.ValidateSessionAsync("eva", issued, None));
        Assert.NotNull(await authenticator.ValidateSessionAsync("eva", rig.Time.GetUtcNow(), None));
        Assert.Null(await authenticator.ValidateSessionAsync("ghost", issued, None));
    }

    [Fact]
    public async Task SeederStoresConfiguredPasswordsOnlyAsHashes()
    {
        var rig = new TestRig();
        await new AccountSeeder([new UserCredential("eva", AccessRole.Operator, "", Password)], rig.AccountStore, rig.Time).SeedAsync(None);

        var account = await rig.AccountStore.FindAsync("eva", None);

        Assert.Null(account!.ApiKeyHash);
        Assert.DoesNotContain(Password, account.PasswordHash);
        Assert.True(PasswordHasher.Verify(Password, account.PasswordHash));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pbkdf2-sha256$210000$AA==$")]
    [InlineData("pbkdf2-sha256$210000$!invalid!$AA==")]
    public Task MissingOrMalformedHashesNeverAuthenticate(string? hash)
    {
        Assert.False(PasswordHasher.Verify(Password, hash));
        return Task.CompletedTask;
    }

    private static async Task<(TestRig Rig, PasswordAuthenticator Authenticator)> CreateAsync(PasswordLockoutOptions? lockout = null)
    {
        var rig = new TestRig();
        await new AccountSeeder([new UserCredential("eva", AccessRole.Operator, "", Password)], rig.AccountStore, rig.Time).SeedAsync(None);
        var authenticator = new PasswordAuthenticator(rig.AccountStore, new AuthenticationThrottle(rig.Time), lockout ?? new PasswordLockoutOptions(),
            rig.Events, rig.Time);
        return (rig, authenticator);
    }
}
