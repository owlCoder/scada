using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;

namespace ProtocolTests.Support;

/// <summary>Fake <see cref="IGridView"/> with a fixed definition and a replaceable snapshot.</summary>
public sealed class Iec104FakeGridView : IGridView
{
    private readonly Lock _sync = new();
    private GridSnapshot _snapshot = Iec104SampleGrid.CreateSnapshot();

    public GridDefinition Definition { get; } = Iec104SampleGrid.CreateDefinition();

    public GridStatus Status { get; } = new(false, TimeSpan.FromSeconds(1), 1, 1, 1, "Sample grid", 1);

    public GridSnapshot Snapshot()
    {
        lock (_sync)
            return _snapshot;
    }

    public bool SetSnapshot(GridSnapshot snapshot)
    {
        lock (_sync)
            _snapshot = snapshot;
        return true;
    }
}
