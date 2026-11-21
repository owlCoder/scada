using Domain.Grid.Snapshots;

namespace Domain.Grid.Network;

public sealed record GridTransition(string Kind, string SourceId, string Message,
    string? Actor = null, GridChangeCause Cause = GridChangeCause.System);
