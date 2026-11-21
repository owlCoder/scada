namespace Domain.Process.Configuration;

/// <summary>Level thresholds in percent of tank capacity.</summary>
public sealed record TankThresholds(double LowLow, double Low, double High, double HighHigh)
{
    public bool IsOrdered =>
        LowLow >= 0 && LowLow < Low && Low < High && High < HighHigh && HighHigh <= 100;
}
