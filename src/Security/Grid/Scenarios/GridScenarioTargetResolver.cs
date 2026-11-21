using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;

namespace Security.Grid.Scenarios;

/// <summary>
/// Resolves generic scenario targets against the running network. Every selector either finds its elements or produces
/// a clear error, so a scenario that cannot find its target fails to start instead of silently doing nothing.
/// </summary>
public static class GridScenarioTargetResolver
{
    public static GridScenarioTargetResolution Resolve(IGridView view, IReadOnlyCollection<GridScenarioTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(targets);
        var definition = view.Definition;
        var snapshot = view.Snapshot();
        var errors = new List<string>();
        var resolved = new Dictionary<GridScenarioTarget, string[]>();

        foreach (var target in targets.Distinct())
        {
            var ids = ResolveKind(view, definition, snapshot, target, errors);
            if (ids is not null)
                resolved[target] = ids;
        }

        return errors.Count > 0 ? GridScenarioTargetResolution.Failed(errors) : GridScenarioTargetResolution.Ok(new GridScenarioTargets(resolved));
    }

    private static string[]? ResolveKind(IGridView view, GridDefinition definition, GridSnapshot snapshot, GridScenarioTarget target,
        List<string> errors)
    {
        switch (target.Kind)
        {
            case GridScenarioTargetKind.FeederBreakers:
                {
                    var lineIds = definition.Lines.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
                    var ids = definition.Switches
                        .Where(s => s.Kind == GridSwitchKind.Breaker && lineIds.Contains(s.ElementId))
                        .OrderByDescending(s => snapshot.Branch(s.ElementId)?.LoadingPercent ?? 0)
                        .ThenBy(s => s.Id, StringComparer.Ordinal)
                        .Select(s => s.Id)
                        .ToArray();
                    return Take(target, ids, "feeder breakers", errors);
                }
            case GridScenarioTargetKind.FeederLines:
                {
                    var feederBreakers = FeederBreakerIds(definition, snapshot);
                    var ids = definition.Lines
                        .Where(l => definition.Switches.Any(s => s.Kind == GridSwitchKind.Breaker && s.ElementId == l.Id && feederBreakers.Contains(s.Id)))
                        .Select(l => l.Id)
                        .OrderByDescending(id => snapshot.Branch(id)?.LoadingPercent ?? 0)
                        .ThenBy(id => id, StringComparer.Ordinal)
                        .ToArray();
                    return Take(target, ids, "feeder lines", errors);
                }
            case GridScenarioTargetKind.MostLoadedFeeder:
                {
                    var branch = snapshot.Branches.Where(b => b.Kind == GridBranchKind.Line)
                        .OrderByDescending(b => b.LoadingPercent).ThenBy(b => b.Id, StringComparer.Ordinal)
                        .FirstOrDefault();
                    return branch is null ? Fail(target, "no line feeder exists", errors) : [branch.Id];
                }
            case GridScenarioTargetKind.FeederProtection:
                {
                    var feederBreakers = FeederBreakerIds(definition, snapshot);
                    var ids = definition.Protections
                        .Where(p => feederBreakers.Contains(p.SwitchId))
                        .OrderBy(p => p.Id, StringComparer.Ordinal)
                        .Select(p => p.Id)
                        .ToArray();
                    return Take(target, ids, "protection relays on feeder breakers", errors);
                }
            case GridScenarioTargetKind.TransformerWithOltc:
                {
                    var transformer = definition.Transformers.Where(t => t.HasOnLoadTapChanger)
                        .OrderBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault();
                    return transformer is null ? Fail(target, "no transformer with an OLTC exists", errors) : [transformer.Id];
                }
            case GridScenarioTargetKind.LargestLoads:
                {
                    var ids = definition.Loads.OrderByDescending(l => l.PeakActivePowerMw).ThenBy(l => l.Id, StringComparer.Ordinal)
                        .Select(l => l.Id).ToArray();
                    return Take(target, ids, "loads", errors);
                }
            case GridScenarioTargetKind.FeederRtu:
                {
                    var feeder = MostLoadedFeederId(snapshot);
                    if (feeder is null) return Fail(target, "no line feeder exists", errors);
                    var rtu = definition.Rtus.Where(r => r.ElementIds.Contains(feeder)
                            || definition.Switches.Any(s => s.ElementId == feeder && r.ElementIds.Contains(s.Id)))
                        .OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
                    return rtu is null ? Fail(target, $"no RTU controls the most loaded feeder {feeder}", errors) : [rtu.Id];
                }
            case GridScenarioTargetKind.RtuBreaker:
                {
                    var feederRtu = ResolveKind(view, definition, snapshot, new GridScenarioTarget(GridScenarioTargetKind.FeederRtu), []);
                    if (feederRtu is null || feederRtu.Length == 0)
                        return Fail(target, "no feeder RTU to take a breaker from", errors);
                    var rtuElements = definition.Rtus.First(r => r.Id == feederRtu[0]).ElementIds.ToHashSet(StringComparer.Ordinal);
                    var breaker = definition.Switches.Where(s => s.Kind == GridSwitchKind.Breaker && rtuElements.Contains(s.Id))
                        .OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
                    return breaker is null ? Fail(target, "the feeder RTU controls no breaker", errors) : [breaker.Id];
                }
            case GridScenarioTargetKind.AnyBreaker:
                {
                    var breaker = definition.Switches.Where(s => s.Kind == GridSwitchKind.Breaker)
                        .OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
                    return breaker is null ? Fail(target, "no breaker exists", errors) : [breaker.Id];
                }
            case GridScenarioTargetKind.AllCapacitors:
                {
                    var ids = definition.Capacitors.Select(c => c.Id).Order(StringComparer.Ordinal).ToArray();
                    return ids.Length == 0 ? Fail(target, "no capacitor bank exists", errors) : ids;
                }
            case GridScenarioTargetKind.FeederCurrentMeasurements:
                {
                    var feeder = MostLoadedFeederId(snapshot);
                    if (feeder is null) return Fail(target, "no line feeder exists", errors);
                    var ids = snapshot.Measurements
                        .Where(m => m.ElementId == feeder && m.Kind == GridMeasurementKind.Current)
                        .OrderBy(m => m.Id, StringComparer.Ordinal).Select(m => m.Id).ToArray();
                    return Take(target, ids, $"current measurements of feeder {feeder}", errors);
                }
            case GridScenarioTargetKind.HighestLoadBusVoltageMeasurement:
                {
                    var bus = definition.Loads.GroupBy(l => l.BusId)
                        .OrderByDescending(g => g.Sum(l => l.PeakActivePowerMw))
                        .Select(g => g.Key).FirstOrDefault();
                    if (bus is null)
                        return Fail(target, "no bus with loads exists", errors);
                    var measurement = snapshot.Measurements
                        .Where(m => m.ElementId == bus && m.Kind == GridMeasurementKind.Voltage)
                        .OrderBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault();
                    return measurement is null ? Fail(target, $"no voltage measurement at the highest-load bus {bus}", errors) : [measurement.Id];
                }
            default:
                Fail(target, $"unknown target kind {target.Kind}", errors);
                return null;
        }
    }

    private static HashSet<string> FeederBreakerIds(GridDefinition definition, GridSnapshot snapshot)
    {
        var lineIds = definition.Lines.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        return definition.Switches
            .Where(s => s.Kind == GridSwitchKind.Breaker && lineIds.Contains(s.ElementId))
            .Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
    }

    private static string? MostLoadedFeederId(GridSnapshot snapshot) =>
        snapshot.Branches.Where(b => b.Kind == GridBranchKind.Line)
            .OrderByDescending(b => b.LoadingPercent).ThenBy(b => b.Id, StringComparer.Ordinal)
            .FirstOrDefault()?.Id;

    private static string[]? Take(GridScenarioTarget target, string[] ids, string what, List<string> errors)
    {
        if (ids.Length < Math.Max(1, target.Count))
            return Fail(target, $"only {ids.Length} of {target.Count} required {what} exist", errors);
        return ids.Take(Math.Max(1, target.Count)).ToArray();
    }

    private static string[]? Fail(GridScenarioTarget target, string reason, List<string> errors)
    {
        errors.Add($"Target {target.Describe()}: {reason}.");
        return null;
    }
}
