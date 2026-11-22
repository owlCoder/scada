using Domain.Grid.Definition;

namespace Hmi.Services.Grid.Sld;

/// <summary>
/// Render-time layout of distribution substations (a 20/0.4 kV transformer with its MV junction, LV consumer bus and
/// load): when the definition positions are too tight or misaligned, the members are grouped into one compact,
/// aligned vertical stack under the feeder line — MV bus on the feeder, transformer, 0.4 kV bus and load(s) at a
/// consistent row gap on the same x. The user's positions stay the base: the MV bus is never moved and a stack that
/// is already spaced out is left untouched. Layout only — the stored definition is never modified.
/// </summary>
public static class SldStackLayout
{
    /// <summary>Design row gap of a distribution stack in SVG units (fits a symbol plus a value box at fit zoom).</summary>
    public const double DesignRowGap = 84;

    /// <summary>Minimum row gap (SVG units) — never squeeze the stack tighter than the plain unit spacing.</summary>
    public const double MinRowGap = 48;

    /// <summary>Clear space kept between the stack bottom and the next element row below (SVG units).</summary>
    private const double RowClearance = 24;

    /// <summary>
    /// Position overrides (SVG units, keyed by element id) for every distribution substation whose definition
    /// positions are too tight (LV bus closer than two design rows) or misaligned (transformer/load off the MV bus
    /// column). The MV bus keeps its position and defines the stack column.
    /// </summary>
    public static IReadOnlyDictionary<string, DiagramPosition> Overrides(
        GridDefinition definition, IReadOnlyDictionary<string, DiagramPosition> render)
    {
        var result = new Dictionary<string, DiagramPosition>(StringComparer.Ordinal);
        var buses = definition.Buses.ToDictionary(b => b.Id);
        foreach (var transformer in definition.Transformers)
        {
            if (!buses.TryGetValue(transformer.LvBusId, out var lv) || lv.NominalVoltageKv >= 1)
                continue; // only 0.4 kV consumer substations stack (primary 110/20 kV transformers keep their layout)
            if (!buses.TryGetValue(transformer.HvBusId, out var hv))
                continue;
            var loads = definition.Loads.Where(l => l.BusId == lv.Id).ToArray();
            if (loads.Length == 0)
                continue;
            if (!render.TryGetValue(hv.Id, out var hvP) || !render.TryGetValue(lv.Id, out var lvP)
                || !render.TryGetValue(transformer.Id, out var trP))
                continue;
            var loadPositions = loads.Select(l => render.GetValueOrDefault(l.Id)).Where(p => p is not null).Cast<DiagramPosition>().ToArray();
            var columnTolerance = SldModelBuilder.UnitX * 0.75;
            var misaligned = Math.Abs(trP.X - hvP.X) > columnTolerance
                || loadPositions.Any(p => Math.Abs(p.X - hvP.X) > columnTolerance);
            var tooTight = lvP.Y - hvP.Y < 2 * DesignRowGap * 0.9;
            if (!tooTight && !misaligned)
                continue; // the user spaced this substation out — keep the positions as they are

            // Room below the MV bus before the next element row in the same column (next feeder, default 5 grid rows).
            var stackIds = new HashSet<string>(StringComparer.Ordinal) { hv.Id, lv.Id, transformer.Id };
            foreach (var load in loads)
                stackIds.Add(load.Id);
            var nextRowY = definition.Buses.Select(b => b.Id).Concat(definition.Sources.Select(s => s.Id))
                .Concat(definition.Transformers.Select(t => t.Id)).Concat(definition.Switches.Select(s => s.Id))
                .Concat(definition.Loads.Select(l => l.Id)).Concat(definition.Generators.Select(g => g.Id))
                .Concat(definition.Capacitors.Select(c => c.Id))
                .Where(id => !stackIds.Contains(id) && render.TryGetValue(id, out _))
                .Select(id => render[id])
                .Where(p => p.Y > hvP.Y + 1 && Math.Abs(p.X - hvP.X) <= columnTolerance)
                .Select(p => p.Y)
                .DefaultIfEmpty(hvP.Y + 5 * SldModelBuilder.UnitY)
                .Min();
            var rows = 2 + loads.Length; // transformer + LV bus + one row per load below the MV bus
            var available = nextRowY - hvP.Y - RowClearance;
            var gap = Math.Clamp(available / rows, MinRowGap, DesignRowGap);
            result[transformer.Id] = new DiagramPosition(hvP.X, hvP.Y + gap);
            result[lv.Id] = new DiagramPosition(hvP.X, hvP.Y + 2 * gap);
            for (var i = 0; i < loads.Length; i++)
                result[loads[i].Id] = new DiagramPosition(hvP.X, hvP.Y + (3 + i) * gap);
        }
        return result;
    }
}
