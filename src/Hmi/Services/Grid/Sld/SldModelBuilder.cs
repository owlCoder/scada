using Domain.Grid.Definition;

namespace Hmi.Services.Grid.Sld;

/// <summary>
/// Builds the static single-line-diagram model from a network definition: element anchors, wire polylines
/// (bus → element switches → element, or the full line path through waypoints and inline switches) and one
/// dashed substation frame per RTU. Pure geometry — no snapshot state.
/// The SLD maps definition grid units with its own, larger spacing than the editor
/// (<see cref="GridDiagramGeometry.Unit"/> = 40 stays the editor geometry): <see cref="UnitX"/> per unit
/// horizontally and <see cref="UnitY"/> vertically, so value boxes fit between the elements at fit zoom.
/// </summary>
public static class SldModelBuilder
{
    /// <summary>SVG units per definition grid unit, horizontally, on the SLD canvas.</summary>
    public const double UnitX = 64;

    /// <summary>SVG units per definition grid unit, vertically, on the SLD canvas.</summary>
    public const double UnitY = 56;

    /// <summary>Small inset around the member anchors only — large padding (or wire endpoints that run to a shared
    /// bus) would swallow neighbouring feeders into one RTU frame.</summary>
    private const double FramePadding = 28;

    public static SldStaticModel Build(GridDefinition definition)
    {
        var render = RenderPositions(definition);

        var elements = new List<SldElement>();
        elements.AddRange(definition.Buses.Select(b => new SldElement(
            b.Id, b.Name, SldElementKind.Bus, render[b.Id], b.NominalVoltageKv, [], null, b.Id, null)));
        elements.AddRange(definition.Lines.Select(l => new SldElement(
            l.Id, l.Name, SldElementKind.Line, render[l.Id], BusVoltage(definition, l.FromBusId),
            [LinePoints(definition, l, render)], null, l.FromBusId, null)));
        elements.AddRange(definition.Transformers.Select(t => new SldElement(
            t.Id, t.Name, SldElementKind.Transformer, render[t.Id], t.HvRatedKv,
            [Stub(definition, t.HvBusId, t.Id, render), Stub(definition, t.LvBusId, t.Id, render)], null, t.HvBusId, null)));
        elements.AddRange(definition.Sources.Select(s => new SldElement(
            s.Id, s.Name, SldElementKind.Source, render[s.Id], BusVoltage(definition, s.BusId),
            [Stub(definition, s.BusId, s.Id, render)], null, s.BusId, null)));
        elements.AddRange(definition.Loads.Select(l => new SldElement(
            l.Id, l.Name, SldElementKind.Load, render[l.Id], BusVoltage(definition, l.BusId),
            [Stub(definition, l.BusId, l.Id, render)], null, l.BusId, null)));
        elements.AddRange(definition.Generators.Select(g => new SldElement(
            g.Id, g.Name, SldElementKind.Generator, render[g.Id], BusVoltage(definition, g.BusId),
            [Stub(definition, g.BusId, g.Id, render)], null, g.BusId, null)));
        elements.AddRange(definition.Capacitors.Select(c => new SldElement(
            c.Id, c.Name, SldElementKind.Capacitor, render[c.Id], BusVoltage(definition, c.BusId),
            [Stub(definition, c.BusId, c.Id, render)], null, c.BusId, null)));
        elements.AddRange(definition.Switches.Select(s => new SldElement(
            s.Id, s.Name, SldElementKind.Switch, render[s.Id], BusVoltage(definition, s.BusId),
            [], s.Kind, s.BusId, s.ElementId)));

        var byId = elements.ToDictionary(e => e.Id);
        var frames = definition.Rtus.Select(rtu => Frame(rtu, elements)).Where(f => f is not null).Cast<SldFrame>().ToArray();
        var points = elements.SelectMany(e => e.Wires.SelectMany(w => w)).Concat(elements.Select(e => e.Anchor));
        return new SldStaticModel(elements, frames, SldBounds.Of(points, 90), byId);
    }

    /// <summary>
    /// SVG positions of every definition element, with the distribution-substation stacks applied
    /// (<see cref="SldStackLayout"/>): anchors and wire endpoints all read from this map, so a stacked substation
    /// moves as one aligned group.
    /// </summary>
    public static IReadOnlyDictionary<string, DiagramPosition> RenderPositions(GridDefinition definition)
    {
        var render = new Dictionary<string, DiagramPosition>(StringComparer.Ordinal);
        foreach (var b in definition.Buses)
            render[b.Id] = At(b.Position);
        foreach (var s in definition.Sources)
            render[s.Id] = At(s.Position);
        foreach (var t in definition.Transformers)
            render[t.Id] = At(t.Position);
        foreach (var s in definition.Switches)
            render[s.Id] = At(s.Position);
        foreach (var l in definition.Loads)
            render[l.Id] = At(l.Position);
        foreach (var g in definition.Generators)
            render[g.Id] = At(g.Position);
        foreach (var c in definition.Capacitors)
            render[c.Id] = At(c.Position);
        foreach (var overridePosition in SldStackLayout.Overrides(definition, render))
            render[overridePosition.Key] = overridePosition.Value;
        foreach (var l in definition.Lines)
            render[l.Id] = Midpoint(LinePoints(definition, l, render)); // LinePoints already maps to SVG units
        return render;
    }

    /// <summary>Full line path: from bus → from-side switches → waypoints → to-side switches → to bus.</summary>
    public static IReadOnlyList<DiagramPosition> LinePoints(
        GridDefinition definition, GridLineDefinition line, IReadOnlyDictionary<string, DiagramPosition>? render = null)
    {
        var from = BusPoint(definition, line.FromBusId, render);
        var to = BusPoint(definition, line.ToBusId, render);
        if (from is null || to is null)
            return [];
        return Dedupe(new[] { from }
            .Concat(definition.Switches.Where(s => s.BusId == line.FromBusId && s.ElementId == line.Id)
                .Select(s => Pos(s.Id, s.Position, render)))
            .Concat(line.Waypoints.Select(At))
            .Concat(definition.Switches.Where(s => s.BusId == line.ToBusId && s.ElementId == line.Id)
                .Select(s => Pos(s.Id, s.Position, render)).Reverse())
            .Append(to));
    }

    /// <summary>Stub path from a bus through the element's switches to the element itself.</summary>
    public static IReadOnlyList<DiagramPosition> Stub(
        GridDefinition definition, string busId, string elementId, IReadOnlyDictionary<string, DiagramPosition>? render = null)
    {
        var bus = BusPoint(definition, busId, render);
        var element = render is not null && render.TryGetValue(elementId, out var p) ? p : At(ElementPosition(definition, elementId));
        if (bus is null)
            return [element];
        return Dedupe(new[] { bus }
            .Concat(definition.Switches.Where(s => s.BusId == busId && s.ElementId == elementId)
                .Select(s => Pos(s.Id, s.Position, render)))
            .Append(element));
    }

    public static DiagramPosition At(DiagramPosition grid) => new(grid.X * UnitX, grid.Y * UnitY);

    public static double BusVoltage(GridDefinition definition, string? busId) =>
        busId is null ? 0 : definition.Buses.FirstOrDefault(b => b.Id == busId)?.NominalVoltageKv ?? 0;

    private static DiagramPosition? BusPoint(GridDefinition definition, string busId, IReadOnlyDictionary<string, DiagramPosition>? render) =>
        render is not null && render.TryGetValue(busId, out var p) ? p : At(definition.Buses.FirstOrDefault(b => b.Id == busId)?.Position ?? new DiagramPosition(0, 0));

    private static DiagramPosition Pos(string id, DiagramPosition grid, IReadOnlyDictionary<string, DiagramPosition>? render) =>
        render is not null && render.TryGetValue(id, out var p) ? p : At(grid);

    private static DiagramPosition ElementPosition(GridDefinition definition, string elementId) =>
        definition.Buses.FirstOrDefault(b => b.Id == elementId)?.Position
        ?? definition.Sources.FirstOrDefault(s => s.Id == elementId)?.Position
        ?? definition.Transformers.FirstOrDefault(t => t.Id == elementId)?.Position
        ?? definition.Switches.FirstOrDefault(s => s.Id == elementId)?.Position
        ?? definition.Loads.FirstOrDefault(l => l.Id == elementId)?.Position
        ?? definition.Generators.FirstOrDefault(g => g.Id == elementId)?.Position
        ?? definition.Capacitors.FirstOrDefault(c => c.Id == elementId)?.Position
        ?? new DiagramPosition(0, 0);

    private static SldFrame? Frame(GridRtuDefinition rtu, IReadOnlyList<SldElement> elements)
    {
        var members = elements.Where(e => rtu.ElementIds.Contains(e.Id)).ToArray();
        if (members.Length == 0)
            return null;
        // Bounding box of the member anchors only: the connecting wires run to (shared) buses outside the station
        // and must not stretch the frame over neighbouring feeders.
        var bounds = SldBounds.Of(members.Select(m => m.Anchor), FramePadding);
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
