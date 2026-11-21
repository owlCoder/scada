using Domain.Grid.Definition;

namespace Hmi.Services.Grid;

/// <summary>Typical training values, not a manufacturer's specification or an engineering design catalogue.</summary>
public sealed class GridEquipmentCatalog
{
    public IReadOnlyList<GridConductorCatalogItem> Conductors { get; } =
    [
        new("Al XLPE 3×150 mm²", GridLineKind.Cable, .206, .10, 250, 300),
        new("Al XLPE 3×240 mm²", GridLineKind.Cable, .125, .09, 300, 400),
        new("Cu XLPE 3×95 mm²", GridLineKind.Cable, .193, .10, 220, 285),
        new("ACSR 95 mm²", GridLineKind.Overhead, .32, .35, 3, 300),
        new("ACSR 150 mm²", GridLineKind.Overhead, .21, .34, 3.2, 440)
    ];
    public IReadOnlyList<GridTransformerCatalogItem> Transformers { get; } =
    [
        new("20/0.4 kV · 250 kVA", .250, 20, .4, 4, 3.25, .3, false),
        new("20/0.4 kV · 400 kVA", .400, 20, .4, 4, 4.6, .43, false),
        new("20/0.4 kV · 630 kVA", .630, 20, .4, 6, 6.5, .6, false),
        new("20/0.4 kV · 1000 kVA", 1, 20, .4, 6, 10.5, .9, false),
        new("110/20 kV · 20 MVA", 20, 110, 20, 12, 100, 18, true),
        new("110/20 kV · 31.5 MVA", 31.5, 110, 20, 12, 135, 23, true),
        new("110/20 kV · 40 MVA", 40, 110, 20, 12, 160, 28, true)
    ];
    public IReadOnlyList<string> Palette { get; } = ["Bus", "Busbar", "Source", "Line", "Cable", "Transformer", "Breaker", "Disconnector", "LoadBreakSwitch", "Load", "PV", "Capacitor", "Protection", "RTU"];
}
