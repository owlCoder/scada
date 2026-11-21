namespace Security.Commands;

public sealed class CommandRateOptions
{
    public int Limit { get; set; } = 10;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
