using Domain.Grid.Snapshots;

namespace Application.Grid;

/// <summary>Something that happened during a step or a command. <see cref="Actor"/> is null for physics and protection.</summary>
public sealed record GridStepEvent(
    GridStepEventKind Kind,
    string SourceId,
    string Message,
    string? Actor,
    GridChangeCause Cause,
    string? Channel = null);
