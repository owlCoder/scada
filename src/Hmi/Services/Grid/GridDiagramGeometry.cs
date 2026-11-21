using System.Globalization;
using Domain.Grid.Definition;

namespace Hmi.Services.Grid;

public static class GridDiagramGeometry
{
    public const double Unit = 40;
    public static IReadOnlyList<GridDiagramNode> Nodes(GridDefinition definition) =>
    [
        .. definition.Buses.Select(x => new GridDiagramNode(x.Id, x.Name, "Bus", x.Position)),
        .. definition.Sources.Select(x => new GridDiagramNode(x.Id, x.Name, "Source", x.Position)),
        .. definition.Transformers.Select(x => new GridDiagramNode(x.Id, x.Name, "Transformer", x.Position)),
        .. definition.Switches.Select(x => new GridDiagramNode(x.Id, x.Name, x.Kind.ToString(), x.Position)),
        .. definition.Loads.Select(x => new GridDiagramNode(x.Id, x.Name, "Load", x.Position)),
        .. definition.Generators.Select(x => new GridDiagramNode(x.Id, x.Name, "PV", x.Position)),
        .. definition.Capacitors.Select(x => new GridDiagramNode(x.Id, x.Name, "Capacitor", x.Position))
    ];

    public static string Point(DiagramPosition p) => $"{N(p.X * Unit)},{N(p.Y * Unit)}";
    public static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    public static string Transform(DiagramPosition p) => $"translate({N(p.X * Unit)} {N(p.Y * Unit)})";
    public static string LinePath(GridDefinition definition, GridLineDefinition line)
    {
        var from = definition.Buses.FirstOrDefault(b => b.Id == line.FromBusId)?.Position;
        var to = definition.Buses.FirstOrDefault(b => b.Id == line.ToBusId)?.Position;
        return from is null || to is null ? "" : string.Join(" ", new[] { from }
            .Concat(definition.Switches.Where(s => s.BusId == line.FromBusId && s.ElementId == line.Id).Select(s => s.Position))
            .Concat(line.Waypoints)
            .Concat(definition.Switches.Where(s => s.BusId == line.ToBusId && s.ElementId == line.Id).Select(s => s.Position).Reverse())
            .Append(to).Select(Point));
    }


    public static IReadOnlyList<string> ConnectedBuses(GridDefinition definition, GridDiagramNode node) => node.Kind switch
    {
        "Source" => definition.Sources.Where(x => x.Id == node.Id).Select(x => x.BusId).ToArray(),
        "Transformer" => definition.Transformers.Where(x => x.Id == node.Id).SelectMany(x => new[] { x.HvBusId, x.LvBusId }).ToArray(),
        "Load" => definition.Loads.Where(x => x.Id == node.Id).Select(x => x.BusId).ToArray(),
        "PV" => definition.Generators.Where(x => x.Id == node.Id).Select(x => x.BusId).ToArray(),
        "Capacitor" => definition.Capacitors.Where(x => x.Id == node.Id).Select(x => x.BusId).ToArray(),
        _ => []
    };

    public static string ConnectionPath(GridDefinition definition, string busId, GridDiagramNode node)
    {
        var bus = definition.Buses.FirstOrDefault(b => b.Id == busId);
        return bus is null ? "" : string.Join(" ", new[] { bus.Position }
            .Concat(definition.Switches.Where(s => s.BusId == busId && s.ElementId == node.Id).Select(s => s.Position))
            .Append(node.Position).Select(Point));
    }

    public static string VoltageClass(double kv) => kv >= 100 ? "voltage-hv" : kv >= 1 ? "voltage-mv" : "voltage-lv";
}
