using Application.Events;
using Application.Identity;
using Application.Realtime;
using Domain.Grid.Commands;
using Domain.Grid.Definition;
using Domain.Grid.Network;
using Domain.Grid.Snapshots;
using Microsoft.Extensions.Logging;

namespace Application.Grid;

/// <summary>Distributes committed world changes outside the engine lock.</summary>
public sealed class GridCoordinator(GridEngine engine, IGridDefinitionStore store, IEventPublisher events,
    IRealtimePublisher realtime, IEnumerable<IGridObserver> observers, TimeProvider time,
    ILogger<GridCoordinator> logger) : IGridRuntime, IGridDefinitionAdministration
{
    private readonly IGridObserver[] _observers = observers.ToArray();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<GridStepReport?> StepAsync(bool force, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        GridStepReport? report;
        try { report = engine.Step(force); }
        finally { _gate.Release(); }
        if (report is not null) await DistributeAsync(report, cancellationToken);
        return report;
    }

    public async Task<GridCommandResult> ApplyAsync(Func<GridWorld, GridCommandResult> mutation, Actor actor, string channel,
        bool training, CancellationToken cancellationToken, string description = "Grid action", string targetId = "grid")
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken);
        GridCommandResult result;
        GridStepReport report;
        try
        {
            result = engine.Mutate(mutation);
            report = engine.CaptureReport();
        }
        finally { _gate.Release(); }
        Audit(result, actor, channel, description);
        report = report with
        {
            Events = [.. report.Events, new(result.Accepted ? GridStepEventKind.CommandAccepted : GridStepEventKind.CommandRejected,
            targetId, description, actor.Name, training ? GridChangeCause.System : GridChangeCause.Operator, channel)]
        };
        await DistributeAsync(report, cancellationToken);
        return result;
    }

    public GridCommandResult Audit(GridCommandResult result, Actor actor, string channel, string description)
    {
        var category = result.Accepted ? EventCategories.Audit : result.Reason == GridCommandRejection.NotAuthorized
            ? EventCategories.AuthorizationDenied : result.Reason == GridCommandRejection.CommunicationLoss
                ? EventCategories.Communication : EventCategories.CommandRejected;
        events.Publish(SecurityEvent.Create(time.GetUtcNow(), category, result.Accepted ? EventSeverity.Information : EventSeverity.Medium,
            $"grid:{channel}", $"{description}: {result.Reason}: {result.Message}", actor.Name));
        return result;
    }

    public async Task<GridCommandResult> SetPausedAsync(bool paused, Actor actor, CancellationToken cancellationToken)
    {
        if (!Authorized(actor, AccessRole.Operator)) return Denied(actor, "Pause/resume requires Operator.");
        cancellationToken.ThrowIfCancellationRequested();
        engine.SetPaused(paused);
        var result = Audit(GridCommandResult.Ok(paused ? "Grid paused." : "Grid resumed."), actor, "runtime", "Pause/resume");
        await DistributeAsync(engine.CaptureReport(), cancellationToken);
        return result;
    }

    public async Task<GridCommandResult> ResetAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (!Authorized(actor, AccessRole.Administrator)) return Denied(actor, "Reset requires Administrator.");
        await _gate.WaitAsync(cancellationToken);
        try { engine.Reset(); }
        finally { _gate.Release(); }
        Audit(GridCommandResult.Ok("Grid reset."), actor, "runtime", "Reset");
        await DistributeAsync(engine.CaptureReport(), cancellationToken);
        return GridCommandResult.Ok("Grid reset.");
    }

    public async Task<GridCommandResult> ActivateVersionAsync(int version, Actor actor, CancellationToken cancellationToken)
    {
        if (!Authorized(actor, AccessRole.Administrator)) return Denied(actor, "Activation requires Administrator.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var saved = await store.GetAsync(version, cancellationToken);
            if (saved is null) return Audit(GridCommandResult.Reject(GridCommandRejection.UnknownTarget, "Unknown definition version."), actor, "runtime", "Activate");
            // Validate and solve before changing either active storage or the live world.
            var candidate = new GridEngine(saved.Definition, engine.Options, time);
            if (!candidate.Snapshot().Converged) return Audit(GridCommandResult.Reject(GridCommandRejection.InvalidValue, "Definition does not converge."), actor, "runtime", "Activate");
            if (!await store.SetActiveAsync(version, cancellationToken)) return Audit(GridCommandResult.Reject(GridCommandRejection.UnknownTarget, "Unknown definition version."), actor, "runtime", "Activate");
            engine.Load(saved.Definition, saved.Version);
        }
        catch (ArgumentException exception)
        { return Audit(GridCommandResult.Reject(GridCommandRejection.InvalidValue, exception.Message), actor, "runtime", "Activate"); }
        finally { _gate.Release(); }
        var report = engine.CaptureReport();
        report = report with { Events = [.. report.Events, new(GridStepEventKind.DefinitionActivated, "grid", $"Definition version {version} activated", actor.Name, GridChangeCause.System)] };
        await DistributeAsync(report, cancellationToken);
        return Audit(GridCommandResult.Ok("Definition activated."), actor, "runtime", "Activate");
    }

    public IReadOnlyList<string> Validate(GridDefinition definition) => GridDefinitionValidator.Validate(definition);

    public async Task<GridDefinitionSaveResult> SaveAsync(GridDefinition definition, string comment, Actor actor, CancellationToken cancellationToken)
    {
        if (!Authorized(actor, AccessRole.Administrator))
        { Denied(actor, "Saving requires Administrator."); return new(false, ["Administrator role required."], null); }
        if (comment.Length > 4096 || actor.Name.Length > 128)
        { Audit(GridCommandResult.Reject(GridCommandRejection.InvalidValue, "Definition metadata too long."), actor, "definitions", "Save"); return new(false, ["Definition metadata too long."], null); }
        var issues = Validate(definition);
        if (GridDefinitionValidator.HasErrors(issues))
        { Audit(GridCommandResult.Reject(GridCommandRejection.InvalidValue, "Definition validation failed."), actor, "definitions", "Save"); return new(false, issues, null); }
        var saved = await store.SaveAsync(definition, actor.Name, comment, cancellationToken);
        Audit(GridCommandResult.Ok($"Definition saved as version {saved.Version}."), actor, "definitions", "Save");
        return new(true, issues, saved.Version);
    }

    private async Task<int> DistributeAsync(GridStepReport report, CancellationToken cancellationToken)
    {
        foreach (var e in report.Events)
        {
            var category = e.Kind is GridStepEventKind.ProtectionTripped or GridStepEventKind.AlarmRaised or GridStepEventKind.SupplyLost
                ? EventCategories.ProcessAlarm : e.Kind == GridStepEventKind.RtuOnlineChanged ? EventCategories.Communication
                : e.Kind is GridStepEventKind.ProtectionSettingChanged or GridStepEventKind.MeasurementSpoofed or GridStepEventKind.FaultInjected
                    ? EventCategories.Scenario : EventCategories.DeviceChange;
            events.Publish(SecurityEvent.Create(report.Timestamp, category,
                e.Kind is GridStepEventKind.ProtectionTripped or GridStepEventKind.SupplyLost ? EventSeverity.High : EventSeverity.Information,
                $"grid:{e.Channel ?? "engine"}", e.Message, e.Actor ?? "grid", e.SourceId));
        }
        var visible = GridViewRedactor.ForOperators(report.Snapshot, engine.Definition);
        realtime.Publish(RealtimeTopics.Grid, visible);
        realtime.Publish(RealtimeTopics.GridAlarms, visible.Alarms);
        realtime.Publish(RealtimeTopics.GridStatus, engine.Status);
        var count = 0;
        foreach (var observer in _observers)
        {
            try { count += await observer.OnGridStepAsync(report, cancellationToken); }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            { logger.LogError(exception, "Grid observer {Observer} failed", observer.GetType().Name); }
        }
        return count;
    }

    private static bool Authorized(Actor actor, AccessRole role) => !string.IsNullOrWhiteSpace(actor.Name) && actor.HasRole(role);
    private GridCommandResult Denied(Actor actor, string message) => Audit(GridCommandResult.Reject(GridCommandRejection.NotAuthorized, message), actor, "runtime", "Authorization");
}
