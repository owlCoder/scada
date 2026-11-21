using Domain.Network;

namespace Domain.Control;

public sealed record FieldReadResult(
    TransmissionOutcome Outcome,
    IReadOnlyDictionary<string, double> Measurements,
    IReadOnlyDictionary<string, ActuatorFeedback> Actuators)
{
    public bool Success => Outcome == TransmissionOutcome.Delivered;

    public static FieldReadResult Failed(TransmissionOutcome outcome) =>
        new(outcome, new Dictionary<string, double>(), new Dictionary<string, ActuatorFeedback>());
}
