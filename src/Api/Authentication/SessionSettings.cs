namespace Api.Authentication;

public sealed class SessionSettings
{
    public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>How often open HMI circuits check whether the account is still valid.</summary>
    public TimeSpan RevalidationInterval { get; set; } = TimeSpan.FromMinutes(1);
}
