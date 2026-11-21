using Domain.Grid.Definition;
using Domain.Grid.Snapshots;

namespace Application.Grid;

/// <summary>Read access to the running network. <see cref="Snapshot"/> is ground truth: redact it with
/// <c>GridViewRedactor.ForOperators</c> before showing it to anyone who is not an Administrator (instructor).</summary>
public interface IGridView
{
    GridDefinition Definition { get; }
    GridStatus Status { get; }
    GridSnapshot Snapshot();
}
