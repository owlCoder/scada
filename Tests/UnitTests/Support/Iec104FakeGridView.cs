using Application.Grid;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;

namespace UnitTests.Support;

/// <summary>Fake <see cref="IGridView"/> with a replaceable definition and snapshot, used by the IEC 104 unit tests.</summary>
public sealed class Iec104FakeGridView : IGridView
{
    private readonly Lock _sync = new();
    private GridDefinition _definition = Iec104SampleGrid.CreateDefinition();
    private GridSnapshot _snapshot = Iec104SampleGrid.CreateSnapshot();

    public GridDefinition Definition
    {
        get { lock (_sync) return _definition; }
    }

    public GridStatus Status { get; } = new(false, TimeSpan.FromSeconds(1), 1, 1, 1, "Sample grid", 1);

    public GridSnapshot Snapshot()
    {
        lock (_sync)
            return _snapshot;
    }

    public bool SetDefinition(GridDefinition definition)
    {
        lock (_sync)
            _definition = definition;
        return true;
    }

    public bool SetSnapshot(GridSnapshot snapshot)
    {
        lock (_sync)
            _snapshot = snapshot;
        return true;
    }
}
