using Application.Detection;
using Application.Events;
using Application.Grid;
using Application.Incidents;
using Application.Realtime;
using Domain.Grid.Definition;
using Infrastructure.Persistence.InMemory;
using Infrastructure.Persistence.Queue;
using Infrastructure.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Security.Grid.Detection;
using Security.Grid.Detection.Rules;

namespace UnitTests.Grid.Templates.Support;

/// <summary>
/// Runs a template on the real <see cref="GridEngine"/> and feeds every step report through the real
/// <see cref="GridDetectionEngine"/> (with all default grid rules), publishing the step events to the event buffer
/// exactly like <c>GridCoordinator.DistributeAsync</c> does. Used to prove a template's normal simulated day is
/// finding-free.
/// </summary>
public sealed class TemplatesGridDayRig
{
    public TemplatesGridDayRig(GridDefinition template, string startTimeOfDay = "08:00", int seed = 104)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Options = new GridOptions { StartTimeOfDay = startTimeOfDay, StepSeconds = 60, TimeScale = 60, RandomSeed = seed };
        Engine = new GridEngine(template, Options, Time);
        Buffer = new RecentEventBuffer();
        var queue = new ChannelPersistenceQueue(new PersistenceQueueOptions { RetryDelay = TimeSpan.Zero }, Time,
            NullLogger<ChannelPersistenceQueue>.Instance);
        Realtime = new RealtimeBroadcaster(Time);
        Events = new EventPublisher(Buffer, queue, Realtime);
        Incidents = new IncidentService(new InMemoryIncidentStore(), Events, Realtime, Time);
        DetectionStore = new InMemoryDetectionStore();
        Detection = new GridDetectionEngine(DefaultGridDetectionRules.Create(Engine), Buffer, DetectionStore, Incidents,
            Events, Realtime, new TemplatesNoActiveScenarios(), Engine, NullLogger<GridDetectionEngine>.Instance);
    }

    public FakeTimeProvider Time { get; }
    public GridOptions Options { get; }
    public GridEngine Engine { get; }
    public RecentEventBuffer Buffer { get; }
    public RealtimeBroadcaster Realtime { get; }
    public EventPublisher Events { get; }
    public IncidentService Incidents { get; }
    public InMemoryDetectionStore DetectionStore { get; }
    public GridDetectionEngine Detection { get; }

    /// <summary>Advances the fake clock and the engine by one step, publishes the step events and evaluates all rules.</summary>
    public async Task<GridStepReport> StepAsync()
    {
        Time.Advance(TimeSpan.FromSeconds(Options.StepSeconds));
        var report = Engine.Step() ?? throw new InvalidOperationException("The engine is paused.");
        foreach (var stepEvent in report.Events)
        {
            var category = stepEvent.Kind is GridStepEventKind.ProtectionTripped or GridStepEventKind.AlarmRaised or GridStepEventKind.SupplyLost
                ? EventCategories.ProcessAlarm : stepEvent.Kind == GridStepEventKind.RtuOnlineChanged ? EventCategories.Communication
                : stepEvent.Kind is GridStepEventKind.ProtectionSettingChanged or GridStepEventKind.MeasurementSpoofed or GridStepEventKind.FaultInjected
                    ? EventCategories.Scenario : EventCategories.DeviceChange;
            var severity = stepEvent.Kind is GridStepEventKind.ProtectionTripped or GridStepEventKind.SupplyLost ? EventSeverity.High : EventSeverity.Information;
            Events.Publish(SecurityEvent.Create(report.Timestamp, category, severity, $"grid:{stepEvent.Channel ?? "engine"}",
                stepEvent.Message, stepEvent.Actor ?? "grid", stepEvent.SourceId));
        }
        await Detection.OnGridStepAsync(report, CancellationToken.None);
        return report;
    }

    public async Task<int> RunDayAsync()
    {
        var steps = 0;
        for (var i = 0; i < 24 * 60; i++)
        {
            await StepAsync();
            steps++;
        }
        return steps;
    }

    public async Task<IReadOnlyList<DetectionRecord>> DetectionsAsync() =>
        await DetectionStore.QueryAsync(new DetectionQuery(Limit: 1000), CancellationToken.None);
}
