namespace Security.Accounts;

/// <summary><see cref="Secret"/> is only populated for rotations and must be shown to the caller once, never logged.</summary>
public sealed record AccountOperationResult(AccountOperationStatus Status, string Message, string? Secret = null)
{
    public bool Succeeded => Status == AccountOperationStatus.Succeeded;
}
