namespace Hmi.Services;

/// <summary>A critical action waiting for the user's confirmation. <see cref="Run"/> returns the message to show afterwards.</summary>
public sealed record PendingAction(string Title, string Description, Func<Task<ActionOutcome>> Run, bool Dangerous = true);
