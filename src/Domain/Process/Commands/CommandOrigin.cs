namespace Domain.Process.Commands;

public sealed record CommandOrigin(ChangeCause Cause, string Actor)
{
    public static CommandOrigin Operator(string actor) => new(ChangeCause.Operator, actor);
    public static CommandOrigin Controller(string controllerId) => new(ChangeCause.Controller, controllerId);
    public static CommandOrigin Interlock => new(ChangeCause.Interlock, "safety-system");
    public static CommandOrigin Unattributed(string actor) => new(ChangeCause.Unattributed, actor);
}
