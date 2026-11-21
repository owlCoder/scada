namespace Security.Detection.Rules;

public sealed record TankBalance(
    string TankId,
    double Capacity,
    string LevelSensorId,
    IReadOnlyList<string> InflowSensors,
    IReadOnlyList<string> OutflowSensors);
