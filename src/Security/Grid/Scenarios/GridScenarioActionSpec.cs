using Application.Identity;
using Domain.Grid.Commands;
using Domain.Grid.Snapshots;

namespace Security.Grid.Scenarios;

/// <summary>
/// One scripted step of a grid training scenario. Actions only go through <see cref="Application.Grid.IGridTraining"/>,
/// <see cref="Application.Grid.IGridControl"/> and read <see cref="Application.Grid.IGridView"/> — exactly the paths an
/// attacker-controlled or careless actor would use.
/// </summary>
public sealed record GridScenarioActionSpec(
    GridScenarioActionKind Kind,
    GridScenarioTarget Target,
    int TargetIndex = 0,
    double? Value = null,
    double? SecondaryValue = null,
    bool? Flag = null,
    GridCommandType? Command = null,
    string? Actor = null,
    AccessRole Role = AccessRole.Operator,
    string Source = "rest",
    long? Sequence = null,
    string? CaptureAs = null,
    GridEquipmentFault? Fault = null)
{
    /// <summary>Short human-readable description used in the run log.</summary>
    public string Describe()
    {
        var target = $"{Target.Describe()}[{TargetIndex}]";
        return Kind switch
        {
            GridScenarioActionKind.ForceSwitch => $"force {target} {(Flag == true ? "closed" : "open")}",
            GridScenarioActionKind.ForceTap => $"force tap of {target} to {Value}",
            GridScenarioActionKind.ForceTapLimit => $"force tap of {target} to {(Flag == true ? "max" : "min")} limit",
            GridScenarioActionKind.RestoreTap => $"restore tap of {target} to initial",
            GridScenarioActionKind.ChangeProtection => $"change protection {target}: enabled={Flag}, pickup={Value}, time={SecondaryValue}",
            GridScenarioActionKind.RestoreProtection => $"restore protection {target} to definition",
            GridScenarioActionKind.InjectFault => $"inject {Fault ?? GridEquipmentFault.ThreePhaseShortCircuit} on {target}",
            GridScenarioActionKind.ClearFault => $"clear fault on {target}",
            GridScenarioActionKind.SpoofMeasurement => Value is null ? $"clear spoof on {target}" : $"spoof {target} = {Value}",
            GridScenarioActionKind.SetRtuOnline => $"set RTU {target} {(Flag == false ? "offline" : "online")}",
            GridScenarioActionKind.Command => $"{Actor ?? "actor"} ({Role}, {Source}): {Command} {target}{(Value is null ? "" : $" {Value}")}",
            GridScenarioActionKind.ReplayCommand => $"{Actor ?? "actor"} ({Role}, {Source}): replay {Command} {target} with captured sequence",
            _ => $"{Kind} {target}"
        };
    }
}
