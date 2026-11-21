using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;

namespace Api.Hosting;

/// <summary>
/// Fallback <see cref="IGridView"/> registered with TryAdd when no grid simulation is wired up in this host (Agent A's
/// grid engine not merged yet). It lets the service graph validate; any actual use fails with a clear error. When a real
/// <see cref="IGridView"/> is registered later, the last registration wins and this stub is never resolved.
/// </summary>
public sealed class UnavailableGridView : IGridView
{
    private const string Message = "Grid mode is not available in this host: no IGridView implementation is registered.";

    public GridDefinition Definition => throw new InvalidOperationException(Message);

    public GridStatus Status => throw new InvalidOperationException(Message);

    public GridSnapshot Snapshot() => throw new InvalidOperationException(Message);
}
