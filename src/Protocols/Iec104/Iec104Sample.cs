namespace Protocols.Iec104;

/// <summary>A point value read from the operator-visible grid snapshot (reported measurement values, never true values).</summary>
public sealed record Iec104Sample(
    Iec104PointKind Kind,
    double? Measurement,
    int? SinglePoint,
    int? DoublePoint,
    int? Step,
    bool StepTransient,
    byte Quality)
{
    public bool SameValue(Iec104Sample other)
    {
        if (Kind != other.Kind || Quality != other.Quality)
            return false;
        return Kind switch
        {
            Iec104PointKind.Measurement => Measurement == other.Measurement,
            Iec104PointKind.SwitchStatus => DoublePoint == other.DoublePoint,
            Iec104PointKind.TapPosition => Step == other.Step && StepTransient == other.StepTransient,
            _ => SinglePoint == other.SinglePoint
        };
    }

    /// <summary>Deadband test for measurements: 0.5 % of the point range plus any quality change; discrete points compare exactly.</summary>
    public bool ExceedsDeadband(Iec104Sample previous, double range) => Kind switch
    {
        Iec104PointKind.Measurement => Quality != previous.Quality || (range > 0
            ? Math.Abs((Measurement ?? 0) - (previous.Measurement ?? 0)) > 0.005 * range
            : Measurement != previous.Measurement),
        _ => !SameValue(previous)
    };
}
