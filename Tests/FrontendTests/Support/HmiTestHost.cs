using Application.Detection;
using Application.Identity;
using Application.Persistence;
using Application.Realtime;
using Application.Registers;
using Application.Training;
using Bunit;
using Hmi.Services;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Security.Detection;
using UnitTests.Support;

namespace FrontendTests.Support;

/// <summary>
/// A bUnit context wired to the real application services of a <see cref="TestRig"/> (in-memory stores, fake clock) and a
/// signed-in user with the given role. Policies follow the role hierarchy, as in the API host.
/// </summary>
public sealed class HmiTestHost : IAsyncDisposable
{
    private HmiTestHost(AccessRole role, string name, RegisterWriteOptions? registerOptions)
    {
        RealtimeBroadcaster? broadcaster = null;
        Rig = new TestRig(registerOptions, createRealtime: time => broadcaster = new RealtimeBroadcaster(time));
        Broadcaster = broadcaster!;
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
        services.AddSingleton(Rig.RegisterOptions);
        services.AddSingleton(Rig.Historian);
        services.AddSingleton(Rig.Buffer);
        services.AddSingleton<Application.Events.IEventStore>(Rig.EventStore);
        services.AddSingleton<Application.Persistence.IPersistenceQueue>(Rig.Queue);
        services.AddSingleton(Rig.Training);
        services.AddSingleton<IPlantView>(Rig.Training);
        services.AddSingleton<IStorageProbe, InMemoryStorageProbe>();
        services.AddSingleton<IDetectionRuleSettingsStore, InMemoryDetectionRuleSettingsStore>();
        services.AddSingleton<DetectionRuleAdministrationService>();
        services.AddSingleton<InstructorTrainingService>();
        services.AddSingleton<Application.Events.IEventPublisher>(Rig.Events);
        services.AddSingleton<TimeProvider>(Rig.Time);
        services.AddSingleton<IRealtimeFeed>(Broadcaster);
        services.AddScoped<HmiSession>();
    }

    public BunitContext Context { get; } = new();
    public TestRig Rig { get; }
    public RealtimeBroadcaster Broadcaster { get; }

    public static HmiTestHost Create(AccessRole role, string name = "tester", RegisterWriteOptions? registerOptions = null) =>
        new(role, name, registerOptions);

    public ValueTask DisposeAsync() => Context.DisposeAsync();
}
