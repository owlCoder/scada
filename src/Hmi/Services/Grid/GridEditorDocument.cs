using Domain.Grid.Definition;

namespace Hmi.Services.Grid;

/// <summary>Per-editor draft. The active network and saved immutable versions are never mutated.</summary>
public sealed class GridEditorDocument
{
    private int _sequence;
    public GridDefinition Definition { get; private set; } = Empty();
    public string? SelectedId { get; private set; }
    public string? ConnectingFrom { get; private set; }
    public string? ConnectingElement { get; private set; }
    public bool Dirty { get; private set; }
    public string Message { get; private set; } = "";
    public IReadOnlyList<GridDiagramNode> Nodes => GridDiagramGeometry.Nodes(Definition);
    public static GridDefinition Empty() => new("Untitled network", 50, [], [], [], [], [], [], [], [], [], []);

    public bool Load(GridDefinition definition)
    {
        Definition = definition; SelectedId = null; ConnectingFrom = null; ConnectingElement = null; Dirty = false; Message = "";
        return true;
    }
    public bool Select(string id) { SelectedId = id; return true; }
    public bool Replace(GridDefinition definition) { Definition = definition; Dirty = true; return true; }
    public bool MarkSaved() { Dirty = false; return true; }
    private string NewId(string prefix)
    {
        string id;
        do { id = $"{prefix}-{++_sequence}"; } while (AllIds().Contains(id));
        return id;
    }
    public IReadOnlyList<string> AllIds() => [.. Nodes.Select(n => n.Id), .. Definition.Lines.Select(l => l.Id), .. Definition.Protections.Select(p => p.Id), .. Definition.Rtus.Select(r => r.Id)];

    public string Add(string kind, DiagramPosition? at = null)
    {
        var p = at ?? new DiagramPosition(2 + Nodes.Count % 6 * 3, 2 + Nodes.Count / 6 * 3);
        p = new DiagramPosition(Math.Round(p.X), Math.Round(p.Y));
        var id = NewId(kind.ToUpperInvariant());
        var bus = Definition.Buses.LastOrDefault()?.Id ?? "";
        Definition = kind switch
        {
            "Bus" or "Busbar" => Definition with { Buses = [.. Definition.Buses, new(id, id, 20, kind == "Busbar" ? GridBusKind.Busbar : GridBusKind.Junction, p)] },
            "Source" => Definition with { Sources = [.. Definition.Sources, new(id, id, bus, 1, 1000, p)] },
            "Line" or "Cable" => Definition with { Lines = [.. Definition.Lines, new(id, id, "", "", kind == "Cable" ? GridLineKind.Cable : GridLineKind.Overhead, 1, .21, .34, 3.2, 440, null, [])] },
            "Transformer" => Definition with { Transformers = [.. Definition.Transformers, new(id, id, "", "", .63, 20, .4, 6, 6.5, .6, false, -2, 2, 0, 0, 2.5, null, 1, 30, null, p)] },
            "Breaker" or "Disconnector" or "LoadBreakSwitch" => Definition with { Switches = [.. Definition.Switches, new(id, id, Enum.Parse<GridSwitchKind>(kind), "", "", true, kind == "Breaker" ? 20 : null, p)] },
            "Load" => Definition with { Loads = [.. Definition.Loads, new(id, id, bus, .1, .95, GridLoadProfile.Residential, 100, false, p)] },
            "PV" => Definition with { Generators = [.. Definition.Generators, new(id, id, bus, GridGeneratorKind.Photovoltaic, .1, p)] },
            "Capacitor" => Definition with { Capacitors = [.. Definition.Capacitors, new(id, id, bus, .1, 4, 0, p)] },
            "Protection" => Definition with { Protections = [.. Definition.Protections, new(id, id, Definition.Switches.FirstOrDefault(s => s.Kind == GridSwitchKind.Breaker)?.Id ?? "", GridProtectionFunction.Overcurrent51, GridCurveType.IecStandardInverse, 400, .1, true)] },
            "RTU" => Definition with { Rtus = [.. Definition.Rtus, new(id, id, Definition.Rtus.Count + 1, null, [])] },
            _ => Definition
        };
        SelectedId = id; Dirty = true;
        if (kind is "Line" or "Cable" or "Transformer" or "Breaker" or "Disconnector" or "LoadBreakSwitch")
        {
            ConnectingElement = id; ConnectingFrom = null;
            Message = "Click the first bus terminal, then a second bus (line/transformer) or an element (switch).";
        }
        return id;
    }

    public bool Move(string id, DiagramPosition p)
    {
        p = new DiagramPosition(Math.Round(p.X), Math.Round(p.Y));
        return Replace(Definition with
        {
            Buses = Definition.Buses.Select(x => x.Id == id ? x with { Position = p } : x).ToArray(),
            Sources = Definition.Sources.Select(x => x.Id == id ? x with { Position = p } : x).ToArray(),
            Transformers = Definition.Transformers.Select(x => x.Id == id ? x with { Position = p } : x).ToArray(),
            Switches = Definition.Switches.Select(x => x.Id == id ? x with { Position = p } : x).ToArray(),
            Loads = Definition.Loads.Select(x => x.Id == id ? x with { Position = p } : x).ToArray(),
            Generators = Definition.Generators.Select(x => x.Id == id ? x with { Position = p } : x).ToArray(),
            Capacitors = Definition.Capacitors.Select(x => x.Id == id ? x with { Position = p } : x).ToArray()
        });
    }

    public bool BeginConnection()
    {
        ConnectingElement = SelectedId; ConnectingFrom = null; Message = "Click a bus terminal, then the destination terminal."; return true;
    }
    public bool CancelConnection() { ConnectingElement = null; ConnectingFrom = null; Message = "Connection cancelled."; return true; }
    public bool Terminal(string id)
    {
        if (ConnectingElement is null) return Select(id);
        if (ConnectingFrom is null)
        {
            if (!Definition.Buses.Any(b => b.Id == id)) { Message = "The first terminal must be a bus."; return false; }
            ConnectingFrom = id; Message = "Click the destination terminal."; return true;
        }
        var target = ConnectingElement;
        if (id == ConnectingFrom) { Message = "Choose a different destination."; return false; }
        if (Definition.Lines.Any(l => l.Id == target) || Definition.Transformers.Any(t => t.Id == target))
        {
            if (!Definition.Buses.Any(b => b.Id == id)) { Message = "The destination must be a bus."; return false; }
            Replace(Definition with
            {
                Lines = Definition.Lines.Select(l => l.Id == target ? l with { FromBusId = ConnectingFrom, ToBusId = id } : l).ToArray(),
                Transformers = Definition.Transformers.Select(t => t.Id == target ? t with { HvBusId = ConnectingFrom, LvBusId = id } : t).ToArray()
            });
        }
        else if (Definition.Switches.Any(s => s.Id == target))
        {
            var valid = Definition.Lines.Any(l => l.Id == id && (l.FromBusId == ConnectingFrom || l.ToBusId == ConnectingFrom))
                || Definition.Transformers.Any(t => t.Id == id && (t.HvBusId == ConnectingFrom || t.LvBusId == ConnectingFrom))
                || Definition.Loads.Any(l => l.Id == id && l.BusId == ConnectingFrom)
                || Definition.Generators.Any(g => g.Id == id && g.BusId == ConnectingFrom)
                || Definition.Capacitors.Any(c => c.Id == id && c.BusId == ConnectingFrom);
            if (!valid) { Message = "Select an element connected to the first bus."; return false; }
            Replace(Definition with { Switches = Definition.Switches.Select(s => s.Id == target ? s with { BusId = ConnectingFrom, ElementId = id } : s).ToArray() });
        }
        else
        {
            Message = "Select a line, transformer or switch before connecting."; return false;
        }
        ConnectingElement = null; ConnectingFrom = null; Message = "Connected."; return true;
    }

    public bool AddWaypoint(DiagramPosition position)
    {
        var p = new DiagramPosition(Math.Round(position.X), Math.Round(position.Y));
        return Replace(Definition with { Lines = Definition.Lines.Select(l => l.Id == SelectedId ? l with { Waypoints = [.. l.Waypoints, p] } : l).ToArray() });
    }
    public bool DeleteSelected()
    {
        var id = SelectedId;
        Replace(Definition with
        {
            Buses = Definition.Buses.Where(x => x.Id != id).ToArray(),
            Sources = Definition.Sources.Where(x => x.Id != id).ToArray(),
            Lines = Definition.Lines.Where(x => x.Id != id).ToArray(),
            Transformers = Definition.Transformers.Where(x => x.Id != id).ToArray(),
            Switches = Definition.Switches.Where(x => x.Id != id).ToArray(),
            Loads = Definition.Loads.Where(x => x.Id != id).ToArray(),
            Generators = Definition.Generators.Where(x => x.Id != id).ToArray(),
            Capacitors = Definition.Capacitors.Where(x => x.Id != id).ToArray(),
            Protections = Definition.Protections.Where(x => x.Id != id).ToArray(),
            Rtus = Definition.Rtus.Where(x => x.Id != id).ToArray()
        });
        SelectedId = null; CancelConnection(); Message = "Deleted. Review any dangling connections in validation."; return true;
    }
}
