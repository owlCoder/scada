using Application.Grid;
using Application.Identity;
using Domain.Grid.Definition;

namespace FrontendTests.Support;

public sealed class HmiFakeGridDefinitionAdministration(HmiFakeGridDefinitionStore store) : IGridDefinitionAdministration
{
    public List<(GridDefinition Definition, string Comment, Actor Actor)> Saves { get; } = [];
    public IReadOnlyList<string> AdditionalErrors { get; set; } = [];
    public IReadOnlyList<string> Validate(GridDefinition definition) =>
    [
        .. AdditionalErrors,
        .. definition.Buses.Count == 0 ? new[] { "At least one bus is required." } : Array.Empty<string>(),
        .. definition.Lines.Where(l => !definition.Buses.Any(b => b.Id == l.FromBusId) || !definition.Buses.Any(b => b.Id == l.ToBusId)).Select(l => $"{l.Id}: both bus terminals must be connected.")
    ];
    public async Task<GridDefinitionSaveResult> SaveAsync(GridDefinition definition, string comment, Actor actor, CancellationToken cancellationToken)
    {
        Saves.Add((definition, comment, actor));
        var errors = Validate(definition);
        if (errors.Count > 0) return new(false, errors, null);
        var version = await store.SaveAsync(definition, actor.Name, comment, cancellationToken);
        return new(true, [], version.Version);
    }
}
