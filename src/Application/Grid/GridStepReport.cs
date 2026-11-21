using Domain.Grid.Snapshots;

namespace Application.Grid;

public sealed record GridStepReport(DateTimeOffset Timestamp, GridSnapshot Snapshot, IReadOnlyList<GridStepEvent> Events);
