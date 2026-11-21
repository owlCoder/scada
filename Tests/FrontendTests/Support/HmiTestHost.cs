using Application.Identity;
using Application.Realtime;
using Application.Training;
using Bunit;
using Hmi.Services;
using Infrastructure.Realtime;
using Microsoft.Extensions.DependencyInjection;
using UnitTests.Support;

namespace FrontendTests.Support;

/// <summary>
/// A bUnit context wired to the real application services of a <see cref="TestRig"/> (in-memory stores, fake clock) and a
/// signed-in user with the given role. Policies follow the role hierarchy, as in the API host.
/// </summary>
public sealed class HmiTestHost : IAsyncDisposable
{
    private HmiTestHost(AccessRole role, string name)
    {
        Rig = new TestRig();
        Broadcaster = new RealtimeBroadcaster(Rig.Time);
        var authorization = Context.AddAuthorization();
        authorization.SetAuthorized(name);
        authorization.SetRoles(role.ToString());
        authorization.SetPolicies(Enum.GetValues<AccessRole>().Where(r => r <= role).Select(r => r.ToString()).ToArray());

        var services = Context.Services;
        services.AddSingleton(Rig.Engine);
        services.AddSingleton(Rig.Coordinator);
        services.AddSingleton(Rig.Commands);
        services.AddSingleton(Rig.Alarms);
        services.AddSingleton(Rig.Incidents);
        services.AddSingleton(Rig.Scenarios);
        services.AddSingleton(Rig.Detection);
        services.AddSingleton(Rig.Accounts);
        services.AddSingleton(Rig.Registers);
        services.AddSingleton(Rig.Historian);
        services.AddSingleton(Rig.Buffer);
        services.AddSingleton<Application.Events.IEventStore>(Rig.EventStore);
        services.AddSingleton<Application.Persistence.IPersistenceQueue>(Rig.Queue);
        services.AddSingleton(Rig.Training);
        services.AddSingleton<IPlantView>(Rig.Training);
        services.AddSingleton<TimeProvider>(Rig.Time);
        services.AddSingleton<IRealtimeFeed>(Broadcaster);
        services.AddScoped<HmiSession>();
    }

    public BunitContext Context { get; } = new();
    public TestRig Rig { get; }
    public RealtimeBroadcaster Broadcaster { get; }

    public static HmiTestHost Create(AccessRole role, string name = "tester") => new(role, name);

    public ValueTask DisposeAsync() => Context.DisposeAsync();
}
