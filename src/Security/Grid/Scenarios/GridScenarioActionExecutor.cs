using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;
using Security.Scenarios;

namespace Security.Grid.Scenarios;

/// <summary>
/// Executes one scripted step through <see cref="IGridTraining"/> / <see cref="IGridControl"/> only. Control commands that
/// the platform rejects (unauthorized, replay, rate limited) still count as successful attack steps; the rejection is
/// recorded in the detail. <c>TargetIndex</c> -1 applies the action to every resolved target id.
/// </summary>
public static class GridScenarioActionExecutor
{
    /// <summary>For <see cref="GridCommandType.SetCapacitorSteps"/>: -1 switches all steps in, -2 restores the initial steps.</summary>
    public const double AllSteps = -1;
    public const double InitialSteps = -2;

    public static async Task<ScenarioActionResult> ExecuteAsync(GridScenarioActionSpec spec, GridScenarioExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var ids = ResolveIds(spec, context);
            if (ids.Length == 0)
                return ScenarioActionResult.Failed($"Target {spec.Target.Describe()} resolved to no elements on this network.");

            var results = new List<string>();
            var failed = 0;
            foreach (var id in ids)
            {
                var result = await ExecuteOneAsync(spec, context, id, cancellationToken);
                results.Add($"{id}: {result}");
                if (!result.StartsWith("accepted", StringComparison.Ordinal) && !result.StartsWith("rejected", StringComparison.Ordinal)
                    && !result.StartsWith("ok", StringComparison.OrdinalIgnoreCase))
                    failed++;
            }

            return failed == 0
                ? ScenarioActionResult.Done(string.Join("; ", results))
                : ScenarioActionResult.Failed(string.Join("; ", results));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ScenarioActionResult.Failed($"Unexpected error: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string[] ResolveIds(GridScenarioActionSpec spec, GridScenarioExecutionContext context) =>
        spec.TargetIndex >= 0
            ? [context.Targets.ResolveOne(spec.Target, spec.TargetIndex)]
            : context.Targets.Resolve(spec.Target).ToArray();

    private static async Task<string> ExecuteOneAsync(GridScenarioActionSpec spec, GridScenarioExecutionContext context, string id,
        CancellationToken cancellationToken) =>
        spec.Kind switch
        {
            GridScenarioActionKind.ForceSwitch => (await context.Training.ForceSwitchAsync(id, spec.Flag == true,
                context.AttackerActor(spec.Actor), cancellationToken)).Accepted ? "ok" : "failed",
            GridScenarioActionKind.ForceTap => (await context.Training.ForceTapAsync(id, (int)(spec.Value ?? 0),
                context.AttackerActor(spec.Actor), cancellationToken)).Accepted ? "ok" : "failed",
            GridScenarioActionKind.ForceTapLimit => (await context.Training.ForceTapAsync(id, TapLimit(context, id, spec),
                context.AttackerActor(spec.Actor), cancellationToken)).Accepted ? "ok" : "failed",
            GridScenarioActionKind.RestoreTap => (await context.Training.ForceTapAsync(id, InitialTap(context, id),
                context.AttackerActor(spec.Actor), cancellationToken)).Accepted ? "ok" : "failed",
            GridScenarioActionKind.ChangeProtection => (await context.Training.ChangeProtectionAsync(id, spec.Value ?? 0,
                spec.SecondaryValue ?? 0, spec.Flag ?? true, context.AttackerActor(spec.Actor), cancellationToken)).Accepted ? "ok" : "failed",
            GridScenarioActionKind.RestoreProtection => (await RestoreProtectionAsync(id, context, spec, cancellationToken)).Accepted ? "ok" : "failed",
            GridScenarioActionKind.InjectFault => (await context.Training.InjectFaultAsync(id,
                spec.Fault ?? GridEquipmentFault.ThreePhaseShortCircuit, context.AttackerActor(spec.Actor), cancellationToken)).Accepted ? "ok" : "failed",
            GridScenarioActionKind.ClearFault => (await context.Training.ClearFaultAsync(id,
                context.AttackerActor(spec.Actor), cancellationToken)).Accepted ? "ok" : "failed",
            GridScenarioActionKind.SpoofMeasurement => (await context.Training.SpoofMeasurementAsync(id, spec.Value,
                context.AttackerActor(spec.Actor), cancellationToken)).Accepted ? "ok" : "failed",
            GridScenarioActionKind.SetRtuOnline => (await context.Training.SetRtuOnlineAsync(id, spec.Flag != false,
                context.AttackerActor(spec.Actor), cancellationToken)).Accepted ? "ok" : "failed",
            GridScenarioActionKind.Command => await CommandAsync(spec, context, id, replay: false, cancellationToken),
            GridScenarioActionKind.ReplayCommand => await CommandAsync(spec, context, id, replay: true, cancellationToken),
            _ => "failed: unknown action kind"
        };

    private static async Task<string> CommandAsync(GridScenarioActionSpec spec, GridScenarioExecutionContext context, string id,
        bool replay, CancellationToken cancellationToken)
    {
        if (spec.Command is not { } commandType)
            return "failed: command action requires a command type";
        var actor = new Actor(spec.Actor ?? (replay ? "replayer" : "actor"), spec.Role);
        long? sequence;
        if (replay)
        {
            sequence = context.Recall(spec.CaptureAs ?? "replay");
            if (sequence is null)
                return $"failed: no captured sequence '{spec.CaptureAs ?? "replay"}' to replay";
        }
        else
        {
            sequence = spec.Sequence ?? context.NextSequence(actor.Name);
            if (spec.CaptureAs is { } key)
                context.Capture(key, sequence.Value);
        }

        var value = ResolveValue(spec, context, id, commandType);
        var result = await context.Control.ExecuteAsync(new GridCommand(commandType, id, value, sequence), actor, spec.Source, cancellationToken);
        return result.Accepted
            ? $"accepted: {result.Message}"
            : $"rejected ({result.Reason}): {result.Message}";
    }

    private static double? ResolveValue(GridScenarioActionSpec spec, GridScenarioExecutionContext context, string id, GridCommandType commandType)
    {
        if (commandType == GridCommandType.SetCapacitorSteps && spec.Value is { } value && value < 0)
        {
            var capacitor = context.View.Definition.Capacitors.FirstOrDefault(c => c.Id == id);
            if (capacitor is null)
                return spec.Value;
            return value == AllSteps ? capacitor.Steps : capacitor.InitialSteps;
        }
        return spec.Value;
    }

    private static int TapLimit(GridScenarioExecutionContext context, string id, GridScenarioActionSpec spec)
    {
        var definition = context.View.Definition.Transformers.First(t => t.Id == id);
        return spec.Flag == true ? definition.TapMax : definition.TapMin;
    }

    private static int InitialTap(GridScenarioExecutionContext context, string id)
    {
        var definition = context.View.Definition.Transformers.First(t => t.Id == id);
        return definition.InitialTap;
    }

    private static Task<GridCommandResult> RestoreProtectionAsync(string id, GridScenarioExecutionContext context,
        GridScenarioActionSpec spec, CancellationToken cancellationToken)
    {
        var definition = context.View.Definition.Protections.First(p => p.Id == id);
        return context.Training.ChangeProtectionAsync(id, definition.PickupValue, definition.TimeSetting, definition.Enabled,
            context.AttackerActor(spec.Actor), cancellationToken);
    }
}
