using Domain.Process.Configuration;

namespace Domain.Process.Devices;

public sealed class Tank
{
    public Tank(TankDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Volume = definition.CapacityCubicMeters * definition.InitialLevelPercent / 100;
    }

    public TankDefinition Definition { get; }
    public string Id => Definition.Id;
    public double Capacity => Definition.CapacityCubicMeters;
    public double Volume { get; private set; }
    public double LevelPercent => Volume / Capacity * 100;
    public bool IsOverflowing { get; private set; }
    public double TotalOverflowVolume { get; private set; }

    /// <summary>Applies a net volume change and returns the volume lost to overflow during this step.</summary>
    public double ApplyVolumeChange(double deltaVolume)
    {
        if (!double.IsFinite(deltaVolume))
            throw new ArgumentOutOfRangeException(nameof(deltaVolume));

        var next = Volume + deltaVolume;
        var overflow = Math.Max(0, next - Capacity);
        Volume = Math.Clamp(next, 0, Capacity);
        IsOverflowing = overflow > 0;
        TotalOverflowVolume += overflow;
        return overflow;
    }

    public double AvailableVolume(double requested) => Math.Clamp(requested, 0, Volume);

    public bool Restore(double volume, double totalOverflow)
    {
        if (!double.IsFinite(volume) || volume < 0 || volume > Capacity)
            return false;

        Volume = volume;
        TotalOverflowVolume = Math.Max(0, totalOverflow);
        IsOverflowing = false;
        return true;
    }
}
