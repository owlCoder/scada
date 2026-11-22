using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;

namespace Hmi.Services.Grid.Workspace;

/// <summary>Resolves element ids against the definition and snapshot and derives the workspace rules that the control
/// window needs: safety tags that block switching, RTU reachability and the element's reported measurements.</summary>
public static class GridWorkspaceElements
{
    private static readonly GridTagKind[] BlockingKinds = [GridTagKind.DoNotOperate, GridTagKind.Earthed, GridTagKind.WorkInProgress];

    public static GridWorkspaceElement? Resolve(GridSnapshot? snapshot, GridDefinition? definition, string id)
    {
        if (definition is null)
            return null;
        if (definition.Buses.FirstOrDefault(b => b.Id == id) is { } bus)
            return new GridWorkspaceElement(id, bus.Name, GridWorkspaceElementKind.Bus, bus.NominalVoltageKv, Bus: bus);
        if (definition.Lines.FirstOrDefault(l => l.Id == id) is { } line)
        {
            var kv = definition.Buses.FirstOrDefault(b => b.Id == line.FromBusId)?.NominalVoltageKv;
            return new GridWorkspaceElement(id, line.Name, GridWorkspaceElementKind.Line, kv, Line: line);
        }
        if (definition.Transformers.FirstOrDefault(t => t.Id == id) is { } transformer)
            return new GridWorkspaceElement(id, transformer.Name, GridWorkspaceElementKind.Transformer, transformer.HvRatedKv, Transformer: transformer);
        if (definition.Switches.FirstOrDefault(s => s.Id == id) is { } switchDefinition)
        {
            var kv = definition.Buses.FirstOrDefault(b => b.Id == switchDefinition.BusId)?.NominalVoltageKv;
            return new GridWorkspaceElement(id, switchDefinition.Name, GridWorkspaceElementKind.Switch, kv, Switch: switchDefinition);
        }
        if (definition.Loads.FirstOrDefault(l => l.Id == id) is { } load)
        {
            var kv = definition.Buses.FirstOrDefault(b => b.Id == load.BusId)?.NominalVoltageKv;
            return new GridWorkspaceElement(id, load.Name, GridWorkspaceElementKind.Load, kv, Load: load);
        }
        if (definition.Generators.FirstOrDefault(g => g.Id == id) is { } generator)
        {
            var kv = definition.Buses.FirstOrDefault(b => b.Id == generator.BusId)?.NominalVoltageKv;
            return new GridWorkspaceElement(id, generator.Name, GridWorkspaceElementKind.Generator, kv);
        }
        if (definition.Capacitors.FirstOrDefault(c => c.Id == id) is { } capacitor)
        {
            var kv = definition.Buses.FirstOrDefault(b => b.Id == capacitor.BusId)?.NominalVoltageKv;
            return new GridWorkspaceElement(id, capacitor.Name, GridWorkspaceElementKind.Capacitor, kv, Capacitor: capacitor);
        }
        if (definition.Protections.FirstOrDefault(p => p.Id == id) is { } protection)
            return new GridWorkspaceElement(id, protection.Name, GridWorkspaceElementKind.Protection, null);
        return null;
    }

    /// <summary>Tags that block supervisory switching of the element: a blocking tag on the element itself, or — for a
    /// switch — on the line/transformer the switch connects.</summary>
    public static IReadOnlyList<GridTag> BlockingTags(string elementId, SwitchSnapshot? switchSnapshot, IReadOnlyList<GridTag> tags)
    {
        var result = new List<GridTag>();
        foreach (var tag in tags)
        {
            if (!BlockingKinds.Contains(tag.Kind))
                continue;
            if (tag.ElementId == elementId || (switchSnapshot is not null && tag.ElementId == switchSnapshot.ElementId))
                result.Add(tag);
        }
        return result;
    }

    /// <summary>True when an RTU that owns the element reports an active communication-loss alarm (the only operator-visible
    /// RTU state in the snapshot).</summary>
    public static bool RtuOffline(string elementId, GridSnapshot? snapshot, GridDefinition? definition)
    {
        if (snapshot is null || definition is null)
            return false;
        var rtuIds = definition.Rtus.Where(r => r.ElementIds.Contains(elementId)).Select(r => r.Id).ToHashSet();
        return rtuIds.Count > 0 && snapshot.Alarms.Any(a => a.Active && a.Type == GridAlarmType.CommunicationLoss && rtuIds.Contains(a.SourceId));
    }

    public static IReadOnlyList<GridMeasurementSnapshot> Measurements(GridSnapshot? snapshot, string elementId) =>
        snapshot?.Measurements.Where(m => m.ElementId == elementId).ToArray() ?? [];

    public static IReadOnlyList<ProtectionSnapshot> ProtectionsOf(GridSnapshot? snapshot, string elementId) =>
        snapshot?.Protections.Where(p => p.SwitchId == elementId || p.Id == elementId).ToArray() ?? [];

    /// <summary>CSS class of the voltage colour for the element's nominal voltage (tokens.css levels).</summary>
    public static string VoltageClass(double? nominalVoltageKv) => nominalVoltageKv switch
    {
        >= 100 => "kv-110",
        >= 30 => "kv-35",
        >= 15 => "kv-20",
        >= 5 => "kv-10",
        _ => "kv-04"
    };
}
