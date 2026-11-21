namespace Security.Scenarios;

/// <summary>
/// One scripted step of a training scenario. Implementations only manipulate the simulated environment or call
/// application services exactly as an attacker or careless operator would; they never touch real systems.
/// </summary>
public interface IScenarioAction
{
    /// <summary>Short human-readable description used in the run log.</summary>
    string Description { get; }

    Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken);
}
