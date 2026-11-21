using Application.Events;
using Application.Identity;
using Application.Persistence;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Persistence.Queue;
using Infrastructure.Realtime;
using Microsoft.Extensions.Time.Testing;
using Security.Accounts;
using Security.Authentication;

namespace UnitTests.Support;

/// <summary>An API key authenticator over an in-memory account store seeded from the given credentials.</summary>
public sealed class AuthenticationHarness
{
    public const string Source = "ip:198.51.100.7";

    private AuthenticationHarness(IReadOnlyDictionary<AccessRole, string> legacyKeys, IReadOnlyList<UserCredential> users)
    {
        Time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var publisher = new EventPublisher(Events, new ChannelPersistenceQueue(new PersistenceQueueOptions(), Time), new NullRealtimePublisher());
        Authenticator = new ApiKeyAuthenticator(new AccessPolicy(legacyKeys), Store, new AuthenticationThrottle(Time), publisher, Time);
        Accounts = new UserAdministrationService(Store, publisher, Time);
        Seeder = new AccountSeeder(users, Store, Time);
    }

    public FakeTimeProvider Time { get; }
    public RecentEventBuffer Events { get; } = new();
    public InMemoryUserAccountStore Store { get; } = new();
    public ApiKeyAuthenticator Authenticator { get; }
    public UserAdministrationService Accounts { get; }
    public AccountSeeder Seeder { get; }

    public static async Task<AuthenticationHarness> CreateAsync(IReadOnlyDictionary<AccessRole, string> legacyKeys, params UserCredential[] users)
    {
        var harness = new AuthenticationHarness(legacyKeys, users);
        await harness.Seeder.SeedAsync(CancellationToken.None);
        return harness;
    }

    public async Task<AuthenticatedUser?> AuthenticateAsync(string? key) =>
        (await Authenticator.AuthenticateAsync(key, Source, CancellationToken.None)).User;

    public async Task<bool> AuthorizeAsync(string? key, AccessRole role) =>
        await AuthenticateAsync(key) is { } user && user.Role >= role;
}
