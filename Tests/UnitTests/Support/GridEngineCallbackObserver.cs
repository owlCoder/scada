using Application.Grid;

namespace UnitTests.Support;

public sealed class GridEngineCallbackObserver(Func<GridStepReport, ValueTask<int>> callback) : IGridObserver
{
    public ValueTask<int> OnGridStepAsync(GridStepReport report, CancellationToken cancellationToken) => callback(report);
}
