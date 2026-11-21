using Domain.Process.Commands;
using Domain.Process.Configuration;

namespace Domain.Process.Devices;

public sealed class Sensor
{
    public Sensor(SensorDefinition definition) =>
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));

    public SensorDefinition Definition { get; }
    public string Id => Definition.Id;
    public SensorFault Fault { get; private set; } = SensorFault.None;
    public double DriftOffset { get; private set; }

    /// <summary>When set, the sensor reports this value instead of the physical one (spoofing attack).</summary>
    public double? SpoofedValue { get; private set; }
    public double TrueValue { get; private set; }
    public double ReportedValue { get; private set; }
    public MeasurementQuality Quality { get; private set; } = MeasurementQuality.Good;

    public bool IsSpoofed => SpoofedValue is not null;
    public DeviceHealth Health => Fault switch
    {
        SensorFault.None => DeviceHealth.Healthy,
        SensorFault.Drift => DeviceHealth.Warning,
        _ => DeviceHealth.Faulted
    };

    public double Update(double trueValue)
    {
        TrueValue = trueValue;
        if (SpoofedValue is { } spoofed)
        {
            ReportedValue = Clamp(spoofed);
            Quality = MeasurementQuality.Good;
            return ReportedValue;
        }

        switch (Fault)
        {
            case SensorFault.Dead:
                ReportedValue = Definition.RangeMin;
                Quality = MeasurementQuality.Bad;
                break;
            case SensorFault.Frozen:
                Quality = MeasurementQuality.Good;
                break;
            case SensorFault.Drift:
                ReportedValue = Clamp(trueValue + DriftOffset);
                Quality = MeasurementQuality.Good;
                break;
            default:
                ReportedValue = Clamp(trueValue);
                Quality = trueValue < Definition.RangeMin || trueValue > Definition.RangeMax
                    ? MeasurementQuality.Uncertain
                    : MeasurementQuality.Good;
                break;
        }

        return ReportedValue;
    }

    public DeviceChange? SetFault(SensorFault fault, double driftOffset)
    {
        if (!double.IsFinite(driftOffset))
            throw new ArgumentOutOfRangeException(nameof(driftOffset));

        if (Fault == fault && Math.Abs(DriftOffset - driftOffset) < double.Epsilon)
            return null;

        var previous = Fault;
        Fault = fault;
        DriftOffset = fault == SensorFault.Drift ? driftOffset : 0;
        return new DeviceChange(Id, "Fault", previous.ToString(), fault.ToString(), ChangeCause.None, "simulation");
    }

    public bool Spoof(double? value)
    {
        if (value is { } v && !double.IsFinite(v))
            return false;

        SpoofedValue = value;
        return true;
    }

    public bool Restore(SensorFault fault, double driftOffset, double? spoofedValue, double reportedValue)
    {
        Fault = fault;
        DriftOffset = driftOffset;
        SpoofedValue = spoofedValue;
        ReportedValue = reportedValue;
        return true;
    }

    private double Clamp(double value) => Math.Clamp(value, Definition.RangeMin, Definition.RangeMax);
}
