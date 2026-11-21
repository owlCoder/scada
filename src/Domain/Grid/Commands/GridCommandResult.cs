namespace Domain.Grid.Commands;

public sealed record GridCommandResult(bool Accepted, GridCommandRejection Reason, string Message)
{
    public static GridCommandResult Ok(string message) => new(true, GridCommandRejection.None, message);
    public static GridCommandResult Reject(GridCommandRejection reason, string message) => new(false, reason, message);
}
