namespace Hmi.Services.Grid.Sld;

/// <summary>Case-insensitive search over element ids and names for the diagram search box.</summary>
public sealed class SldSearchIndex
{
    private readonly IReadOnlyList<SldElement> _elements;

    public SldSearchIndex(SldStaticModel model) => _elements = model.Elements;

    public IReadOnlyList<SldElement> Search(string? query, int max = 8)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        var q = query.Trim();
        return _elements
            .Where(e => e.Id.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .Take(max)
            .ToArray();
    }
}
