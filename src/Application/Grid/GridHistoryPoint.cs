using Domain.Process.Devices;

namespace Application.Grid;

/// <summary>One historian sample of a REPORTED measurement value (never ground truth).</summary>
public sealed record GridHistoryPoint(TimeSpan SimulationTime, DateTimeOffset Timestamp, double Value, MeasurementQuality Quality);
