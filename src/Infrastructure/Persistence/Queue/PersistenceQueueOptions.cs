namespace Infrastructure.Persistence.Queue;

public sealed class PersistenceQueueOptions
{
    public int Capacity { get; set; } = 10_000;
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);
    public int BatchSize { get; set; } = 256;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Capacity is < 1 or > 1_000_000)
            errors.Add("Persistence:Capacity must be between 1 and 1000000.");
        if (MaxAttempts is < 1 or > 10)
            errors.Add("Persistence:MaxAttempts must be between 1 and 10.");
        if (RetryDelay < TimeSpan.Zero || RetryDelay > TimeSpan.FromSeconds(30))
            errors.Add("Persistence:RetryDelay must be between 0 and 30 seconds.");
        if (BatchSize is < 1 or > 10_000)
            errors.Add("Persistence:BatchSize must be between 1 and 10000.");
        return errors;
    }
}
