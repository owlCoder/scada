namespace Application.Grid;

/// <summary>Receives every grid step after it was applied (grid detection, grid scenarios, IEC 104 spontaneous data).</summary>
public interface IGridObserver
{
    ValueTask<int> OnGridStepAsync(GridStepReport report, CancellationToken cancellationToken);
}
