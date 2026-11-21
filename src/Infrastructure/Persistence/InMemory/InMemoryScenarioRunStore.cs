using Application.Scenarios;

namespace Infrastructure.Persistence.InMemory;

public sealed class InMemoryScenarioRunStore(int capacity = 1000) : IScenarioRunStore
{
    private readonly Lock _sync = new();
    private readonly Dictionary<Guid, ScenarioRunRecord> _runs = [];

    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    public Task<bool> SaveAsync(ScenarioRunRecord run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        lock (_sync)
        {
            _runs[run.RunId] = run;
            foreach (var old in _runs.Values.OrderByDescending(r => r.StartedAt).Skip(Capacity).Select(r => r.RunId).ToArray())
                _runs.Remove(old);
        }

        return Task.FromResult(true);
    }

    public Task<ScenarioRunRecord?> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        lock (_sync)
            return Task.FromResult(_runs.GetValueOrDefault(runId));
    }

    /// <summary>Newest first.</summary>
    public Task<IReadOnlyList<ScenarioRunRecord>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        lock (_sync)
            return Task.FromResult<IReadOnlyList<ScenarioRunRecord>>(
                _runs.Values.OrderByDescending(r => r.StartedAt).Take(Math.Max(1, limit)).ToArray());
    }
}
