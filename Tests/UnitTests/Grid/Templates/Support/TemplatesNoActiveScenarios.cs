using Security.Grid.Detection;

namespace UnitTests.Grid.Templates.Support;

/// <summary>No grid scenario run is active while a template's normal day is simulated, so detections are attributed to live operation.</summary>
public sealed class TemplatesNoActiveScenarios : IGridActiveScenarioProvider
{
    public Guid? ActiveRunId => null;
}
