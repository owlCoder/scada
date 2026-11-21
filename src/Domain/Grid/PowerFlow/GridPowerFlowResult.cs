using System.Numerics;

namespace Domain.Grid.PowerFlow;

public sealed record GridPowerFlowResult(bool Converged, int Iterations, IReadOnlyDictionary<string, Complex> Voltages,
    IReadOnlyList<GridBranchModel> Branches, Complex[] NodeVoltages, IReadOnlyDictionary<string, int> BusIndices,
    double SourceImportMw, double SourceLossesMw, IReadOnlyDictionary<string, double> EarthCurrents);
