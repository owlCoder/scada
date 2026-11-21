namespace Security.Detection;

public sealed class MetricsAccumulator
{
    public long Evaluations { get; set; }
    public long Findings { get; set; }
    public long Suppressed { get; set; }
    public double TotalMilliseconds { get; set; }
    public DateTimeOffset? LastFindingAt { get; set; }
}
