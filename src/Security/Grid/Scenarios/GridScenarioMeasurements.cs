using Application.Grid;
using Domain.Grid.Snapshots;

namespace Security.Grid.Scenarios;

/// <summary>Generic grid measurements accumulated over a scenario run, independent of the scenario script.</summary>
public sealed class GridScenarioMeasurements
{
    private int _maxCustomersLost;
    private double _maxLoadingPercent;
    private double _minVoltagePu = double.MaxValue;
    private double _maxVoltagePu = double.MinValue;
    private int _alarmsRaised;
    private int _unattributedChanges;
    private int _loadsShed;

    public GridScenarioMeasurements(GridSnapshot initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ObserveSnapshot(initial);
    }

    public int Observe(GridStepReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _alarmsRaised += report.Events.Count(e => e.Kind == GridStepEventKind.AlarmRaised);
        _unattributedChanges += report.Events.Count(e => e.Cause == GridChangeCause.Unattributed);
        ObserveSnapshot(report.Snapshot);
        return report.Snapshot.Branches.Count;
    }

    private int ObserveSnapshot(GridSnapshot snapshot)
    {
        _maxCustomersLost = Math.Max(_maxCustomersLost, snapshot.Totals.Customers - snapshot.Totals.CustomersSupplied);
        _loadsShed = Math.Max(_loadsShed, snapshot.Loads.Count(l => l.Shed));
        foreach (var branch in snapshot.Branches)
            _maxLoadingPercent = Math.Max(_maxLoadingPercent, branch.LoadingPercent);
        foreach (var bus in snapshot.Buses.Where(b => b.Energized))
        {
            _minVoltagePu = Math.Min(_minVoltagePu, bus.VoltagePu);
            _maxVoltagePu = Math.Max(_maxVoltagePu, bus.VoltagePu);
        }
        return snapshot.Buses.Count;
    }

    public IReadOnlyDictionary<string, double> ToDictionary()
    {
        var result = new SortedDictionary<string, double>(StringComparer.Ordinal)
        {
            ["alarmsRaised"] = _alarmsRaised,
            ["unattributedChanges"] = _unattributedChanges,
            ["maxCustomersLost"] = _maxCustomersLost,
            ["loadsShed"] = _loadsShed,
            ["maxLoadingPercent"] = Math.Round(_maxLoadingPercent, 2)
        };
        if (_minVoltagePu != double.MaxValue)
        {
            result["minVoltagePu"] = Math.Round(_minVoltagePu, 4);
            result["maxVoltagePu"] = Math.Round(_maxVoltagePu, 4);
        }
        return result;
    }
}
