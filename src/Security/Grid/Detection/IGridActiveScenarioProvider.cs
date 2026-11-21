namespace Security.Grid.Detection;

/// <summary>The grid scenario engine exposes the active run so detections can be correlated to it.</summary>
public interface IGridActiveScenarioProvider
{
    Guid? ActiveRunId { get; }
}
