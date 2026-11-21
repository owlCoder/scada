namespace Application.Grid;

/// <summary>
/// A tag on a switch, line, transformer or load. <see cref="GridTagKind.DoNotOperate"/>, <see cref="GridTagKind.Earthed"/>
/// and <see cref="GridTagKind.WorkInProgress"/> block supervisory switching of the tagged element (and of switches connected
/// to a tagged line/transformer); <see cref="GridTagKind.Information"/> is a note only.
/// </summary>
public sealed record GridTag(
    string Id,
    string ElementId,
    GridTagKind Kind,
    string Reason,
    string PlacedBy,
    DateTimeOffset PlacedAt);
