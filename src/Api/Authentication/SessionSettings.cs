namespace Api.Authentication;

public sealed class SessionSettings
{
    public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromHours(8);
}
