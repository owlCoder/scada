namespace Application.Commands;

/// <summary>
/// A supervisory control request. <see cref="SequenceNumber"/> is optional; when supplied it must strictly
/// increase per actor so captured commands cannot be replayed.
/// </summary>
public sealed record OperatorCommand(
    string DeviceId,
    OperatorCommandType Type,
    double? Value,
    CommandSource Source,
    long? SequenceNumber = null);
