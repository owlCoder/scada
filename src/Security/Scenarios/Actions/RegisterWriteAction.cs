using Application.Identity;

namespace Security.Scenarios.Actions;

/// <summary>A protocol-level holding register write. A null actor models an unauthenticated client.</summary>
public sealed class RegisterWriteAction(byte unitId, ushort address, ushort value, Actor? actor, string source) : IScenarioAction
{
    public string Description => $"Write unit {unitId} HR{address} = {value} as {actor?.Name ?? "anonymous"} from {source}";

    public Task<ScenarioActionResult> ExecuteAsync(ScenarioExecutionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ScenarioActionResults.Issued(context.Registers.WriteSetpoint(unitId, address, value, actor, source)));
}
