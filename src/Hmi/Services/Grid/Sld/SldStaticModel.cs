namespace Hmi.Services.Grid.Sld;

/// <summary>Static geometry of the whole network (rebuilt only when the definition changes).</summary>
public sealed record SldStaticModel(
    IReadOnlyList<SldElement> Elements,
    IReadOnlyList<SldFrame> Frames,
    SldBounds Bounds,
    IReadOnlyDictionary<string, SldElement> ById)
{
    public SldElement? Element(string id) => ById.TryGetValue(id, out var element) ? element : null;
}
