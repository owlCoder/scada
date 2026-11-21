using Microsoft.AspNetCore.Components;

namespace Hmi.Services;

public abstract class ConfirmedComponentBase : RealtimeComponentBase
{
    protected PendingAction? Pending { get; private set; }
    protected ActionOutcome? Outcome { get; private set; }

    protected bool Ask(string title, string description, Func<Task<ActionOutcome>> run, bool dangerous = true)
    {
        Pending = new PendingAction(title, description, run, dangerous);
        return true;
    }

    protected bool Ask(string title, string description, Func<ActionOutcome> run, bool dangerous = true) =>
        Ask(title, description, () => Task.FromResult(run()), dangerous);

    protected async Task CompleteAsync(ActionOutcome? outcome)
    {
        Pending = null;
        if (outcome is not null)
        {
            Outcome = outcome;
            await RefreshNowAsync();
        }
    }
}
