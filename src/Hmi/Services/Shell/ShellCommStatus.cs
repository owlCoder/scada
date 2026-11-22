namespace Hmi.Services.Shell;

/// <summary>Communication health of the current mode: healthy RTUs (grid) or links (water) out of the modelled total.</summary>
public sealed record ShellCommStatus(string Kind, int Healthy, int Total)
{
    public bool AllHealthy => Total > 0 && Healthy == Total;
}
