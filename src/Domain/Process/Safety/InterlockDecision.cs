namespace Domain.Process.Safety;

public sealed record InterlockDecision(bool Permitted, string? Reason)
{
    public static InterlockDecision Allow { get; } = new(true, null);
    public static InterlockDecision Block(string reason) => new(false, reason);
}
