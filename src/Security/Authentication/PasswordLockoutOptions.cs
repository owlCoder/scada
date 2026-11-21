namespace Security.Authentication;

public sealed class PasswordLockoutOptions
{
    public int MaxFailedAttempts { get; set; } = 5;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (MaxFailedAttempts is < 1 or > 100)
            errors.Add("Security:Lockout:MaxFailedAttempts must be between 1 and 100.");
        if (LockoutDuration < TimeSpan.FromSeconds(1) || LockoutDuration > TimeSpan.FromDays(1))
            errors.Add("Security:Lockout:LockoutDuration must be between 1 second and 1 day.");
        return errors;
    }
}
