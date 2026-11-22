using Domain.Process.Devices;
using Domain.Process.Snapshots;

namespace Hmi.Services;

/// <summary>The historian tag that represents a device on the P&amp;ID (its level sensor for tanks, flow sensor otherwise).</summary>
public static class PidTags
{
    public static string? PrimaryTag(PlantSnapshot plant, string deviceId)
    {
        ArgumentNullException.ThrowIfNull(plant);
        if (plant.Sensor(deviceId) is not null)
            return deviceId;
        var kind = plant.Tank(deviceId) is not null ? SensorKind.Level : SensorKind.Flow;
        return plant.Sensors.FirstOrDefault(s => s.DeviceId == deviceId && s.Kind == kind)?.Id;
    }
}
