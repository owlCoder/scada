using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;

namespace FrontendTests.Support;

public sealed class HmiFakeGridControl(HmiFakeGridView view) : IGridControl
{
    public List<(GridCommand Command, Actor Actor, string Source)> Calls { get; } = [];
    public Task<GridCommandResult> ExecuteAsync(GridCommand command, Actor actor, string source, CancellationToken cancellationToken)
    {
        Calls.Add((command, actor, source));
        view.State = view.State with
        {
            Switches = view.State.Switches.Select(s => s.Id != command.TargetId ? s : command.Type switch
            {
                GridCommandType.SelectOpen => s with { Selected = true, SelectedToClose = false },
                GridCommandType.SelectClose => s with { Selected = true, SelectedToClose = true },
                GridCommandType.Cancel => s with { Selected = false, SelectedToClose = null },
                GridCommandType.Execute => s with { Closed = s.SelectedToClose ?? s.Closed, Selected = false, SelectedToClose = null },
                _ => s
            }).ToArray(),
            Alarms = view.State.Alarms.Select(a => a.Key == command.TargetId && command.Type == GridCommandType.AcknowledgeAlarm ? a with { Acknowledged = true, AcknowledgedBy = actor.Name } : a).ToArray(),
            Branches = view.State.Branches.Select(b => b.Id == command.TargetId && command.Type == GridCommandType.TapRaise ? b with { TapPosition = b.TapPosition + 1 } : b).ToArray()
        };
        return Task.FromResult(GridCommandResult.Ok("Command accepted."));
    }
}
