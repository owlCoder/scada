using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;

namespace UnitTests.Grid.Support;

/// <summary>A minimal <see cref="IGridView"/> over a fixed definition, for tag-store tests that need many elements
/// without running an engine.</summary>
public sealed class DispatcherFakeGridView(GridDefinition definition) : IGridView
{
    public GridDefinition Definition { get; } = definition;
    public GridStatus Status { get; } = new(false, TimeSpan.Zero, 0, 1, 1, definition.Name, 1);
    public GridSnapshot Snapshot() => throw new NotSupportedException("The fake view only serves the definition.");
}
