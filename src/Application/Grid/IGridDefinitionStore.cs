using Domain.Grid.Definition;

namespace Application.Grid;

/// <summary>Versioned storage of network definitions saved by the editor. Versions are immutable; one is active.</summary>
public interface IGridDefinitionStore
{
    Task<GridDefinitionVersion?> GetActiveAsync(CancellationToken cancellationToken);
    Task<GridDefinitionVersion?> GetAsync(int version, CancellationToken cancellationToken);
    Task<IReadOnlyList<GridDefinitionVersion>> ListAsync(CancellationToken cancellationToken);
    Task<GridDefinitionVersion> SaveAsync(GridDefinition definition, string createdBy, string comment, CancellationToken cancellationToken);
    Task<bool> SetActiveAsync(int version, CancellationToken cancellationToken);
}
