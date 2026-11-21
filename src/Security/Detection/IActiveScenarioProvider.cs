namespace Security.Detection;

public interface IActiveScenarioProvider
{
    Guid? ActiveRunId { get; }
}
