using System.Numerics;

namespace Domain.Grid.PowerFlow;

public sealed record GridPowerFlowResult(bool Converged, int Iterations, IReadOnlyDictionary<string, Complex> Voltages,
    IReadOnlyList<GridBranchModel> Branches, Complex[] NodeVoltages, IReadOnlyDictionary<string, int> BusIndices,
    double SourceImportMw, double SourceLossesMw, IReadOnlyDictionary<string, double> EarthCurrents)
{
    public IReadOnlyDictionary<string, double> LoadPowersMw { get; init; } = new Dictionary<string, double>();
    public IReadOnlyDictionary<string, double> GeneratorPowersMw { get; init; } = new Dictionary<string, double>();
    public bool FaultActive { get; init; }
    public IReadOnlySet<string> EnergizedBuses { get; init; } = new HashSet<string>();
    public Complex[] PrefaultVoltages { get; init; } = [];
}
