using Application.Simulation;
using Domain.Process.Alarms;
using Domain.Process.Commands;
using Domain.Process.Snapshots;

namespace Security.Scenarios;

/// <summary>Generic process measurements accumulated over a scenario run, independent of the scenario script.</summary>
public sealed class ScenarioMeasurements
{
    private readonly Dictionary<string, double> _initialOverflow;
    private readonly Dictionary<string, double> _overflow;
    private readonly Dictionary<string, double> _maxLevel;
    private readonly Dictionary<string, double> _minLevel;
    private int _alarmsRaised;
    private int _interlockTrips;
    private int _safetyViolations;
    private int _unattributedChanges;

    public ScenarioMeasurements(PlantSnapshot initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _initialOverflow = initial.Tanks.ToDictionary(t => t.Id, t => t.TotalOverflowVolume, StringComparer.Ordinal);
        _overflow = initial.Tanks.ToDictionary(t => t.Id, _ => 0d, StringComparer.Ordinal);
        _maxLevel = initial.Tanks.ToDictionary(t => t.Id, t => t.LevelPercent, StringComparer.Ordinal);
        _minLevel = initial.Tanks.ToDictionary(t => t.Id, t => t.LevelPercent, StringComparer.Ordinal);
    }

    public int Observe(SimulationStepReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        foreach (var tank in report.Plant.Snapshot.Tanks)
        {
            _maxLevel[tank.Id] = Math.Max(_maxLevel.GetValueOrDefault(tank.Id, tank.LevelPercent), tank.LevelPercent);
            _minLevel[tank.Id] = Math.Min(_minLevel.GetValueOrDefault(tank.Id, tank.LevelPercent), tank.LevelPercent);
            _overflow[tank.Id] = Math.Max(0, tank.TotalOverflowVolume - _initialOverflow.GetValueOrDefault(tank.Id));
        }

        _alarmsRaised += report.Plant.AlarmTransitions.Count(t => t.Kind == AlarmTransitionKind.Raised);
        _interlockTrips += report.Plant.Interlocks.Count;
        _safetyViolations += report.Plant.SafetyViolations.Count;
        _unattributedChanges += report.Plant.DeviceChanges.Count(c => c.Cause == ChangeCause.Unattributed);
        return report.Plant.Snapshot.Tanks.Count;
    }

    public IReadOnlyDictionary<string, double> ToDictionary()
    {
        var result = new SortedDictionary<string, double>(StringComparer.Ordinal)
        {
            ["alarmsRaised"] = _alarmsRaised,
            ["interlockTrips"] = _interlockTrips,
            ["safetyViolations"] = _safetyViolations,
            ["unattributedChanges"] = _unattributedChanges,
            ["overflowVolumeM3"] = Math.Round(_overflow.Values.Sum(), 3)
        };
        foreach (var (tank, level) in _maxLevel)
            result[$"{tank}.maxLevelPercent"] = Math.Round(level, 3);
        foreach (var (tank, level) in _minLevel)
            result[$"{tank}.minLevelPercent"] = Math.Round(level, 3);
        return result;
    }
}
