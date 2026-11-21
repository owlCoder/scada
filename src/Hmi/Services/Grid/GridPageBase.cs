using Application.Grid;
using Application.Realtime;
using Domain.Grid.Commands;
using Domain.Grid.Definition;
using Domain.Grid.Snapshots;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Hmi.Services.Grid;

public abstract class GridPageBase : ConfirmedComponentBase
{
    [Inject] protected IServiceProvider Services { get; set; } = default!;
    protected GridSnapshot? Snapshot { get; private set; }
    protected GridDefinition? Definition => Session.GridView?.Definition;
    protected GridStatus? Status => Session.GridView?.Status;
    protected override IReadOnlyCollection<string> Topics => [RealtimeTopics.Grid, RealtimeTopics.GridAlarms, RealtimeTopics.GridStatus];

    protected override bool Refresh()
    {
        Snapshot = Session.GridView is null ? null : Session.GridSnapshot();
        return true;
    }

    protected bool Command(GridCommandType type, string id, double? value = null) =>
        Ask($"{type}: {id}?", "This changes the live grid for all operators.", async () =>
        {
            var result = await Services.GetRequiredService<IGridControl>().ExecuteAsync(new GridCommand(type, id, value), Session.Actor, "hmi", StoppingToken);
            return ActionOutcome.From(result);
        });

    protected bool Training(string title, Func<IGridTraining, Task<GridCommandResult>> run) =>
        Ask(title, "Instructor action: this changes the live training network.", async () =>
            ActionOutcome.From(await run(Services.GetRequiredService<IGridTraining>())));

    protected static string Number(double value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
