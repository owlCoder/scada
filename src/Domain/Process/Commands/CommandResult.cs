namespace Domain.Process.Commands;

public sealed record CommandResult(bool Accepted, string DeviceId, CommandRejectionReason Reason, string Message)
{
    public static CommandResult Success(string deviceId, string message) =>
        new(true, deviceId, CommandRejectionReason.None, message);

    public static CommandResult Rejected(string deviceId, CommandRejectionReason reason, string message) =>
        new(false, deviceId, reason, message);
}
