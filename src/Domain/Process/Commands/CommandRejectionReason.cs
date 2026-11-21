namespace Domain.Process.Commands;

public enum CommandRejectionReason
{
    None,
    UnknownDevice,
    InvalidValue,
    InterlockActive,
    DeviceFaulted,
    ModeMismatch,
    NotAuthorized,
    RateLimited,
    ReplayDetected,
    CommunicationUnavailable
}
