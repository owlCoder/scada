using Application.Simulation;
using Domain.Control;
using Domain.Process.Devices;
using Domain.Process.Snapshots;

namespace Protocols.OpcUa.Nodes;

internal static class PlantNodeValues
{
    public static IReadOnlyList<string> Folders { get; } = ["Tanks", "Pumps", "Valves", "Sensors", "Controllers"];

    public static IEnumerable<PlantDevice> Map(PlantSnapshot snapshot, IReadOnlyList<ControllerSnapshot> controllers)
    {
        var plant = PlantViewRedactor.ForOperators(snapshot);
        var measurements = plant.Sensors.ToLookup(sensor => (sensor.DeviceId, sensor.Kind));
        foreach (var tank in plant.Tanks)
        {
            var values = new Dictionary<string, object> { ["Capacity"] = tank.Capacity };
            if (measurements.Contains((tank.Id, SensorKind.Level)))
            {
                values["Level"] = tank.LevelPercent;
                values["Volume"] = tank.Volume;
            }
            yield return new PlantDevice("Tanks", tank.Id, tank.Name, values);
        }

        foreach (var pump in plant.Pumps)
        {
            var values = new Dictionary<string, object>
            {
                ["State"] = pump.State.ToString(),
                ["Mode"] = pump.Mode.ToString(),
                ["Health"] = pump.Health.ToString()
            };
            AddMeasurement(values, "Flow", measurements[(pump.Id, SensorKind.Flow)]);
            AddMeasurement(values, "DischargePressure", measurements[(pump.Id, SensorKind.Pressure)]);
            yield return new PlantDevice("Pumps", pump.Id, pump.Name, values);
        }

        foreach (var valve in plant.Valves)
        {
            var values = new Dictionary<string, object>
            {
                ["Position"] = valve.Position,
                ["Setpoint"] = valve.Setpoint,
                ["Mode"] = valve.Mode.ToString(),
                ["Health"] = valve.Health.ToString()
            };
            AddMeasurement(values, "Flow", measurements[(valve.Id, SensorKind.Flow)]);
            yield return new PlantDevice("Valves", valve.Id, valve.Name, values);
        }

        foreach (var sensor in plant.Sensors)
            yield return new PlantDevice("Sensors", sensor.Id, sensor.Name, new Dictionary<string, object>
            {
                ["Value"] = sensor.ReportedValue,
                ["Unit"] = sensor.Unit,
                ["Quality"] = sensor.Quality.ToString(),
                ["Health"] = sensor.Health.ToString()
            });

        foreach (var controller in controllers)
            yield return new PlantDevice("Controllers", controller.Id, controller.Name, new Dictionary<string, object>
            {
                ["State"] = controller.State.ToString(),
                ["FieldCommunication"] = controller.FieldCommunication.ToString(),
                ["SupervisoryCommunication"] = controller.SupervisoryCommunication.ToString()
            });
    }

    private static bool AddMeasurement(Dictionary<string, object> values, string name, IEnumerable<SensorSnapshot> sensors) =>
        sensors.FirstOrDefault() is { } sensor && values.TryAdd(name, sensor.ReportedValue);
}
