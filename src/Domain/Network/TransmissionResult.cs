namespace Domain.Network;

public sealed record TransmissionResult(string LinkId, TransmissionOutcome Outcome, double LatencyMilliseconds)
{
    public bool Delivered => Outcome == TransmissionOutcome.Delivered;
}
