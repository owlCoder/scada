using Domain.Grid.Definition;

namespace Hmi.Services.Grid.Sld;

/// <summary>
/// Builds the static single-line-diagram model from a network definition: element anchors, wire polylines
/// (bus → element switches → element, or the full line path through waypoints and inline switches) and one
/// dashed substation frame per RTU. Pure geometry — no snapshot state.
/// </summary>
public static class SldModelBuilder
{
    private const double Unit = GridDiagramGeometry.Unit;
    private const double FramePadding = 70;

    public static SldStaticModel Build(GridDefinition definition)
    {
        var elements = new List<SldElement>();
        elements.AddRange(definition.Buses.Select(b => new SldElement(
            b.Id, b.Name, SldElementKind.Bus, At(b.Position), b.NominalVoltageKv, [], null, b.Id, null)));
        elements.AddRange(definition.Lines.Select(l => new SldElement(
            l.Id, l.Name, SldElementKind.Line, Midpoint(LinePoints(definition, l)), BusVoltage(definition, l.FromBusId),
            [LinePoints(definition, l)], null, l.FromBusId, null)));
        elements.AddRange(definition.Transformers.Select(t => new SldElement(
            t.Id, t.Name, SldElementKind.Transformer, At(t.Position), t.HvRatedKv,
            [Stub(definition, t.HvBusId, t.Id, t.Position), Stub(definition, t.LvBusId, t.Id, t.Position)], null, t.HvBusId, null)));
        elements.AddRange(definition.Sources.Select(s => new SldElement(
            s.Id, s.Name, SldElementKind.Source, At(s.Position), BusVoltage(definition, s.BusId),
            [Stub(definition, s.BusId, s.Id, s.Position)], null, s.BusId, null)));
        elements.AddRange(definition.Loads.Select(l => new SldElement(
            l.Id, l.Name, SldElementKind.Load, At(l.Position), BusVoltage(definition, l.BusId),
            [Stub(definition, l.BusId, l.Id, l.Position)], null, l.BusId, null)));
        elements.AddRange(definition.Generators.Select(g => new SldElement(
            g.Id, g.Name, SldElementKind.Generator, At(g.Position), BusVoltage(definition, g.BusId),
            [Stub(definition, g.BusId, g.Id, g.Position)], null, g.BusId, null)));
        elements.AddRange(definition.Capacitors.Select(c => new SldElement(
            c.Id, c.Name, SldElementKind.Capacitor, At(c.Position), BusVoltage(definition, c.BusId),
            [Stub(definition, c.BusId, c.Id, c.Position)], null, c.BusId, null)));
        elements.AddRange(definition.Switches.Select(s => new SldElement(
            s.Id, s.Name, SldElementKind.Switch, At(s.Position), BusVoltage(definition, s.BusId),
            [], s.Kind, s.BusId, s.ElementId)));

        var byId = elements.ToDictionary(e => e.Id);
        var frames = definition.Rtus.Select(rtu => Frame(rtu, elements)).Where(f => f is not null).Cast<SldFrame>().ToArray();
        var points = elements.SelectMany(e => e.Wires.SelectMany(w => w)).Concat(elements.Select(e => e.Anchor));
        return new SldStaticModel(elements, frames, SldBounds.Of(points, 90), byId);
    }

    /// <summary>Full line path: from bus → from-side switches → waypoints → to-side switches → to bus.</summary>
    public static IReadOnlyList<DiagramPosition> LinePoints(GridDefinition definition, GridLineDefinition line)
    {
        var from = definition.Buses.FirstOrDefault(b => b.Id == line.FromBusId)?.Position;
        var to = definition.Buses.FirstOrDefault(b => b.Id == line.ToBusId)?.Position;
        if (from is null || to is null)
            return [];
        return Dedupe(new[] { from }
            .Concat(definition.Switches.Where(s => s.BusId == line.FromBusId && s.ElementId == line.Id).Select(s => s.Position))
            .Concat(line.Waypoints)
            .Concat(definition.Switches.Where(s => s.BusId == line.ToBusId && s.ElementId == line.Id).Select(s => s.Position).Reverse())
            .Append(to)
            .Select(At));
    }

    /// <summary>Stub path from a bus through the element's switches to the element itself.</summary>
    public static IReadOnlyList<DiagramPosition> Stub(GridDefinition definition, string busId, string elementId, DiagramPosition elementPosition)
    {
        var bus = definition.Buses.FirstOrDefault(b => b.Id == busId)?.Position;
        if (bus is null)
            return [At(elementPosition)];
        return Dedupe(new[] { bus }
            .Concat(definition.Switches.Where(s => s.BusId == busId && s.ElementId == elementId).Select(s => s.Position))
            .Append(elementPosition)
            .Select(At));
    }

    public static DiagramPosition At(DiagramPosition grid) => new(grid.X * Unit, grid.Y * Unit);

    public static double BusVoltage(GridDefinition definition, string? busId) =>
        busId is null ? 0 : definition.Buses.FirstOrDefault(b => b.Id == busId)?.NominalVoltageKv ?? 0;

    private static SldFrame? Frame(GridRtuDefinition rtu, IReadOnlyList<SldElement> elements)
    {
        var members = elements.Where(e => rtu.ElementIds.Contains(e.Id)).ToArray();
        if (members.Length == 0)
            return null;
        var points = members.SelectMany(e => e.Wires.SelectMany(w => w)).Concat(members.Select(e => e.Anchor));
        var bounds = SldBounds.Of(points, FramePadding);
        return new SldFrame(rtu.Id, rtu.Name, bounds.X, bounds.Y, bounds.Width, bounds.Height, members.Select(m => m.Id).ToArray());
    }

    private static DiagramPosition Midpoint(IReadOnlyList<DiagramPosition> path)
    {
        if (path.Count == 0)
            return new DiagramPosition(0, 0);
        var (point, _) = SldPathMath.PointAt(path, 0.5);
        return point;
    }

    private static IReadOnlyList<DiagramPosition> Dedupe(IEnumerable<DiagramPosition> points)
    {
        var result = new List<DiagramPosition>();
        foreach (var point in points)
            if (result.Count == 0 || result[^1] != point)
                result.Add(point);
        return result;
    }
}
