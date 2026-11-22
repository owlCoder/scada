using Application.Grid;

namespace Hmi.Services.Grid.Workspace;

/// <summary>One selected measurement series of the trend dock. <see cref="CssClass"/> is the token-based colour class
/// (trend-s0 .. trend-s3) of the series in the chart and legend.</summary>
public sealed record GridTrendSeries(
    string MeasurementId,
    string Label,
    string Unit,
    string CssClass,
    IReadOnlyList<GridHistoryPoint> Points);
