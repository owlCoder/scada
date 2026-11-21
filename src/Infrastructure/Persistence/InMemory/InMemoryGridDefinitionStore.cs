using System.Text.Json;
using Application.Grid;
using Domain.Grid.Definition;

namespace Infrastructure.Persistence.InMemory;

public sealed class InMemoryGridDefinitionStore(TimeProvider time) : IGridDefinitionStore
{
    private readonly Lock _sync = new();
    private readonly List<GridDefinitionVersion> _versions = [new(1, DefaultGridDefinition.Create(), "system", time.GetUtcNow(), "Default distribution network", true)];

    public Task<GridDefinitionVersion?> GetActiveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync) return Task.FromResult(Clone(_versions.Single(v => v.Active)));
    }
    public Task<GridDefinitionVersion?> GetAsync(int version, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync) return Task.FromResult(Clone(_versions.FirstOrDefault(v => v.Version == version)));
    }
    public Task<IReadOnlyList<GridDefinitionVersion>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync) return Task.FromResult<IReadOnlyList<GridDefinitionVersion>>(_versions.Select(v => Clone(v)!).OrderByDescending(v => v.Version).ToArray());
    }
    public Task<GridDefinitionVersion> SaveAsync(GridDefinition definition, string createdBy, string comment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var saved = new GridDefinitionVersion(_versions.Max(v => v.Version) + 1, definition, createdBy, time.GetUtcNow(), comment, false);
            _versions.Add(Clone(saved)!);
            return Task.FromResult(Clone(saved)!);
        }
    }
    public Task<bool> SetActiveAsync(int version, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_versions.All(v => v.Version != version)) return Task.FromResult(false);
            for (var i = 0; i < _versions.Count; i++) _versions[i] = _versions[i] with { Active = _versions[i].Version == version };
            return Task.FromResult(true);
        }
    }
    private static GridDefinitionVersion? Clone(GridDefinitionVersion? value) => value is null ? null
        : value with { Definition = JsonSerializer.Deserialize<GridDefinition>(JsonSerializer.Serialize(value.Definition))! };
}
