using Domain.Grid.Snapshots;

namespace Domain.Grid.Network;

public sealed class GridSwitchState(bool closed)
{
    public long Operations { get; set; }
    public bool Closed { get; set; } = closed;
    public bool? SelectedToClose { get; set; }
    public DateTimeOffset? SelectedUntil { get; set; }
    public string? SelectedBy { get; set; }
    public string? LastOperatedBy { get; set; }
    public GridChangeCause LastCause { get; set; } = GridChangeCause.System;
}
