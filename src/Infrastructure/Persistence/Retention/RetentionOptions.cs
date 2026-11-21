namespace Infrastructure.Persistence.Retention;

/// <summary>How long each retention target keeps data. <see cref="MaxAge"/> overrides <see cref="DefaultMaxAge"/> by target name.</summary>
public sealed class RetentionOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan DefaultMaxAge { get; set; } = TimeSpan.FromDays(30);
    public Dictionary<string, TimeSpan> MaxAge { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan MaxAgeFor(string target) => MaxAge.TryGetValue(target, out var age) ? age : DefaultMaxAge;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Interval < TimeSpan.FromSeconds(1) || Interval > TimeSpan.FromDays(7))
            errors.Add("Retention:Interval must be between 1 second and 7 days.");
        if (DefaultMaxAge < TimeSpan.FromMinutes(1))
            errors.Add("Retention:DefaultMaxAge must be at least 1 minute.");
        foreach (var (target, age) in MaxAge)
        {
            if (age < TimeSpan.FromMinutes(1))
                errors.Add($"Retention:MaxAge:{target} must be at least 1 minute.");
        }

        return errors;
    }
}
