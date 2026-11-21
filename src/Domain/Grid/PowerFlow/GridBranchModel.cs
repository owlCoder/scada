using System.Numerics;
using Domain.Grid.Snapshots;

namespace Domain.Grid.PowerFlow;

/// <summary>Per-unit pi-model: tap is on the from terminal; shunts include magnetising loss.</summary>
public sealed record GridBranchModel(string Id, int From, int To, Complex Impedance, double Tap,
    Complex ShuntFrom, Complex ShuntTo, GridBranchKind Kind, double Rating)
{
    public Complex Yff => (1 / Impedance + ShuntFrom) / (Tap * Tap);
    public Complex Yft => -1 / Impedance / Tap;
    public Complex Ytt => 1 / Impedance + ShuntTo;
    public Complex FromCurrent(Complex[] voltage) => Yff * voltage[From] + Yft * voltage[To];
    public Complex ToCurrent(Complex[] voltage) => Yft * voltage[From] + Ytt * voltage[To];
}
