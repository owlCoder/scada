using Application.Grid;
using Domain.Grid.Definition;

namespace FrontendTests.Support;

public sealed class HmiFakeGridDefinitionStore(HmiFakeGridView view, TimeProvider time) : IGridDefinitionStore
{
    public List<GridDefinitionVersion> Versions { get; } = [new(1, view.Definition, "teacher", time.GetUtcNow(), "Initial network", true), new(2, view.Definition, "teacher", time.GetUtcNow(), "Extended network", false)];
    public Task<GridDefinitionVersion?> GetActiveAsync(CancellationToken cancellationToken) => Task.FromResult(Versions.FirstOrDefault(v => v.Active));
    public Task<GridDefinitionVersion?> GetAsync(int version, CancellationToken cancellationToken) => Task.FromResult(Versions.FirstOrDefault(v => v.Version == version));
    public Task<IReadOnlyList<GridDefinitionVersion>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GridDefinitionVersion>>(Versions.ToArray());
    public Task<GridDefinitionVersion> SaveAsync(GridDefinition definition, string createdBy, string comment, CancellationToken cancellationToken)
    {
        var version = new GridDefinitionVersion(Versions.Max(v => v.Version) + 1, definition, createdBy, time.GetUtcNow(), comment, false);
        Versions.Add(version);
        return Task.FromResult(version);
    }
    public Task<bool> SetActiveAsync(int version, CancellationToken cancellationToken) => Task.FromResult(true);
}
