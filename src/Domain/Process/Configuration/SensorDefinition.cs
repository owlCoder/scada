using Domain.Process.Devices;

namespace Domain.Process.Configuration;

/// <summary>Level sensors measure a tank, flow sensors a pump or valve, pressure sensors a pump discharge.</summary>
public sealed record SensorDefinition(
    string Id,
    string Name,
    SensorKind Kind,
    string DeviceId,
    string Unit,
    double RangeMin,
    double RangeMax);
