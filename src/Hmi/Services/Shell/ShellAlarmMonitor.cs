using System.Globalization;
using Application.Alarms;
using Application.Common;
using Application.Grid;
using Application.Simulation;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;
using Domain.Network;
using Domain.Process.Alarms;
using Microsoft.Extensions.DependencyInjection;

namespace Hmi.Services.Shell;

// HmiSession and ActionOutcome resolve from the parent Hmi.Services namespace.

/// <summary>
/// Mode-aware alarm and communication view for the shell (top bar + alarm bar). Reads the current mode's snapshot —
/// redacted for operators through <see cref="HmiSession"/> — and acknowledges through the existing services, which
/// enforce the Operator role and audit every call.
/// </summary>
public sealed class ShellAlarmMonitor(
    HmiSession session,
    AlarmService alarmService,
    SimulationEngine engine,
    IServiceProvider services)
{
    /// <summary>Newest unacknowledged alarms of the current mode, highest priority first, newest first within a priority.</summary>
    public IReadOnlyList<ShellAlarmItem> UnacknowledgedNewest(int max) => RawAlarms()
        .Where(a => !a.Acknowledged)
        .OrderByDescending(a => a.Severity)
        .ThenByDescending(a => a.SortKey)
        .Take(max)
        .Select(a => new ShellAlarmItem(a.Key, a.SourceId, a.Severity, a.Message, a.RaisedAtText, a.Active, a.Acknowledged, a.AcknowledgedBy))
        .ToArray();

    /// <summary>Unacknowledged alarm counts per priority (P1..P4) of the current mode.</summary>
    public IReadOnlyList<(AlarmSeverity Severity, int Count)> UnacknowledgedCounts()
    {
        var unacknowledged = RawAlarms().Where(a => !a.Acknowledged).ToArray();
        return Enum.GetValues<AlarmSeverity>()
            .OrderByDescending(s => s)
            .Select(s => (s, unacknowledged.Count(a => a.Severity == s)))
            .ToArray();
    }

    /// <summary>Acknowledges one alarm through the current mode's service (Operator only; audited there).</summary>
    public async Task<ActionOutcome> AcknowledgeAsync(string key, CancellationToken cancellationToken)
    {
        if (!session.CanOperate)
            return ActionOutcome.Failed("Only operators can acknowledge alarms.");
        if (session.Mode == SimulationMode.Grid)
        {
            var control = services.GetService<IGridControl>();
            if (control is null)
                return ActionOutcome.Failed("Grid control is not available.");
            var result = await control.ExecuteAsync(new GridCommand(GridCommandType.AcknowledgeAlarm, key), session.Actor, "hmi", cancellationToken);
            return ActionOutcome.From(result);
        }
        return alarmService.Acknowledge(key, session.Actor)
            ? ActionOutcome.Done("Alarm acknowledged.")
            : ActionOutcome.Failed("Alarm acknowledgement failed.");
    }

    /// <summary>Healthy RTUs (grid) or links (water) of the current mode's snapshot.</summary>
    public ShellCommStatus Communication()
    {
        if (session.Mode == SimulationMode.Grid)
        {
            if (session.GridView is not { } view)
                return new ShellCommStatus("RTU", 0, 0);
            var snapshot = session.GridSnapshot();
            var offline = snapshot.Alarms
                .Where(a => a.Type == GridAlarmType.CommunicationLoss && a.Active)
                .Select(a => a.SourceId)
                .ToHashSet(StringComparer.Ordinal);
            var rtus = view.Definition.Rtus;
            return new ShellCommStatus("RTU", rtus.Count(r => !offline.Contains(r.Id)), rtus.Count);
        }
        var links = engine.Read(world => world.Network.Snapshot().Links);
        return new ShellCommStatus("Link", links.Count(l => l.State == LinkState.Up), links.Count);
    }

    /// <summary>Current-mode alarms as raw records; grid alarms carry simulation time, water alarms wall-clock time.</summary>
    private IReadOnlyList<RawAlarm> RawAlarms()
    {
        if (session.Mode == SimulationMode.Grid)
        {
            if (session.GridView is null)
                return [];
            return session.GridSnapshot().Alarms
                .Select(a => new RawAlarm(a.Key, a.SourceId, a.Severity, a.Message,
                    DateTimeOffset.MinValue + a.RaisedAt, a.Active, a.Acknowledged, a.AcknowledgedBy,
                    a.RaisedAt.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)))
                .ToArray();
        }
        return alarmService.GetCurrent()
            .Select(a => new RawAlarm(a.Key, a.SourceId, a.Severity, a.Message,
                a.RaisedAt, a.IsActive, a.IsAcknowledged, a.AcknowledgedBy,
                a.RaisedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)))
            .ToArray();
    }

    private sealed record RawAlarm(
        string Key,
        string SourceId,
        AlarmSeverity Severity,
        string Message,
        DateTimeOffset SortKey,
        bool Active,
        bool Acknowledged,
        string? AcknowledgedBy,
        string RaisedAtText);
}
