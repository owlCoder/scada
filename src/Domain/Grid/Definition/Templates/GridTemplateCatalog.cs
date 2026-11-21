namespace Domain.Grid.Definition.Templates;

/// <summary>
/// The built-in network templates an administrator can load into the grid editor as a starting point. Every entry
/// produces a validator-clean definition that converges in the engine, resolves all grid attack scenario targets and
/// runs a finding-free simulated day under the grid detection rules.
/// </summary>
public static class GridTemplateCatalog
{
    public static IReadOnlyList<GridTemplateCatalogEntry> All { get; } =
    [
        new("default", "Central European distribution grid (default)",
            "110/20/0.4 kV training network: one 31.5 MVA transformer, three feeders, eleven substations, two PV plants, " +
            "one capacitor bank and a normally-open tie switch.",
            DefaultGridDefinition.Create),
        new(UrbanCableGridTemplate.TemplateId, "Urban 20 kV cable grid (2 x 40 MVA)",
            "One 110/20 kV substation with two 40 MVA transformers and a bus coupler; four cable feeders in two open rings " +
            "with normally-open ring points; 14 compact 1000/630 kVA substations, commercial-heavy load, 20 kV capacitor bank.",
            UrbanCableGridTemplate.Create),
        new(RuralOverheadGridTemplate.TemplateId, "Rural 20 kV overhead grid (20 MVA, PV parks)",
            "One 110/20 kV 20 MVA transformer with OLTC/AVR; three long overhead feeders with ten pole-mounted 100/250/400 kVA " +
            "substations; two large PV parks causing reverse power at midday; reclosing-style feeder protection (50/51/51N).",
            RuralOverheadGridTemplate.Create)
    ];

    public static GridTemplateCatalogEntry? Find(string id) =>
        All.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
}
