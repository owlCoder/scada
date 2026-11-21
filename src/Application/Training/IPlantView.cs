using Domain.Process.Snapshots;

namespace Application.Training;

/// <summary>Read-only access to the current plant state (ground truth; redact before showing it to operators).</summary>
public interface IPlantView
{
    PlantSnapshot Snapshot();
}
