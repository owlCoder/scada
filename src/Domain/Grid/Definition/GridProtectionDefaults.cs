namespace Domain.Grid.Definition;

/// <summary>Additive local LV breaker equivalents and coordinated MV neutral backup for built-in networks.</summary>
public static class GridProtectionDefaults
{
    public static GridDefinition Add(GridDefinition definition)
    {
        var switches = definition.Switches.ToList();
        var protections = definition.Protections.ToList();
        var rtus = definition.Rtus.ToArray();
        foreach (var t in definition.Transformers)
        {
            var added = new List<string>();
            if (t.LvRatedKv < 1)
            {
                var id = $"{t.Id}-LV-Q";
                var relay = $"{id}-51";
                switches.Add(new(id, $"{t.Name} LV breaker", GridSwitchKind.Breaker, t.LvBusId, t.Id,
                    true, 50, t.Position with { Y = t.Position.Y + 0.4 }));
                var rated = t.RatedPowerMva * 1000 / (Math.Sqrt(3) * t.LvRatedKv);
                protections.Add(new(relay, $"{t.Name} local overcurrent equivalent", id,
                    GridProtectionFunction.Overcurrent51, GridCurveType.DefiniteTime, 1.5 * rated, 0.15, true));
                added.AddRange([id, relay]);
            }
            else
            {
                foreach (var sw in switches.Where(s => s.ElementId == t.Id && s.BusId == t.LvBusId && s.Kind == GridSwitchKind.Breaker))
                {
                    var id = $"{sw.Id}-51N";
                    if (protections.Any(p => p.Id == id)) continue;
                    protections.Add(new(id, $"{t.Name} neutral backup", sw.Id,
                        GridProtectionFunction.EarthFault51N, GridCurveType.DefiniteTime, 75, 0.8, true));
                    added.Add(id);
                }
            }
            var owner = Array.FindIndex(rtus, r => r.ElementIds.Contains(t.Id));
            if (owner >= 0) rtus[owner] = rtus[owner] with { ElementIds = [.. rtus[owner].ElementIds, .. added] };
        }
        return definition with { Switches = switches, Protections = protections, Rtus = rtus };
    }
}
