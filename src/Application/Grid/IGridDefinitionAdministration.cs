using Application.Identity;
using Domain.Grid.Definition;

namespace Application.Grid;

/// <summary>
/// Editor back end. <see cref="Validate"/> is side-effect free (used for live validation); <see cref="SaveAsync"/>
/// requires the Administrator role, validates, stores a new immutable version and audits. Activate a saved version with
/// <see cref="IGridRuntime.ActivateVersionAsync"/>.
/// </summary>
public interface IGridDefinitionAdministration
{
    IReadOnlyList<string> Validate(GridDefinition definition);
    Task<GridDefinitionSaveResult> SaveAsync(GridDefinition definition, string comment, Actor actor, CancellationToken cancellationToken);
}
