namespace Application.Grid;

/// <summary>
/// In-memory historian of reported grid measurements (bounded ring buffer per measurement), used for trends in the
/// control window and the trends dock. Values are what RTUs report, so they are safe to show to operators.
/// </summary>
public interface IGridHistory
{
    IReadOnlyList<string> MeasurementIds { get; }
    IReadOnlyList<GridHistoryPoint> Series(string measurementId, int maxPoints);
}
