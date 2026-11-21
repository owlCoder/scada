namespace Application.Scenarios;

public interface IScenarioRunStore
{
    Task<bool> SaveAsync(ScenarioRunRecord run, CancellationToken cancellationToken);
    Task<ScenarioRunRecord?> GetAsync(Guid runId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScenarioRunRecord>> ListAsync(int limit, CancellationToken cancellationToken);
}
