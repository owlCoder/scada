using Application.Scenarios;
using Infrastructure.Persistence.InMemory;

namespace UnitTests.Support;

public sealed class FailingScenarioRunStore : IScenarioRunStore
{
    private readonly InMemoryScenarioRunStore _store = new();
    public bool Fail { get; set; }
    public bool Throw { get; set; }

    public Task<bool> SaveAsync(ScenarioRunRecord run, CancellationToken cancellationToken) => Throw
        ? throw new InvalidOperationException("scenario store unavailable")
        : Fail ? Task.FromResult(false) : _store.SaveAsync(run, cancellationToken);
    public Task<ScenarioRunRecord?> GetAsync(Guid runId, CancellationToken cancellationToken) => _store.GetAsync(runId, cancellationToken);
    public Task<IReadOnlyList<ScenarioRunRecord>> ListAsync(int limit, CancellationToken cancellationToken) => _store.ListAsync(limit, cancellationToken);
}
