using Application.Events;
using Application.Identity;
using Application.Simulation;
using Domain.Process.Commands;

namespace Application.Training;

public sealed class InstructorTrainingService(
    ITrainingEnvironment training, SimulationEngine engine, IEventPublisher events, TimeProvider time)
{
    public CommandResult SetControllerRunning(string id, bool running, Actor actor) =>
        Execute(id, actor, running ? "Controller started." : "Controller stopped.",
            "Controller is unavailable or already in the requested state.", () => training.SetControllerRunning(id, running));

    public CommandResult ResetController(string id, Actor actor) =>
        Execute(id, actor, "Controller reset.", "Controller could not be reset.", () => training.ResetController(id));

    public CommandResult ReleaseForces(string id, Actor actor) =>
        Execute(id, actor, "Register forces released.", "Controller not found.", () =>
        {
            if (!engine.Read(world => world.FindController(id) is not null))
                return false;
            training.ReleaseRegisterForces(id);
            return true;
        });

    public CommandResult SetLinkConnected(string id, bool connected, Actor actor) =>
        Execute(id, actor, connected ? "Link connected." : "Link disconnected.",
            "Link is unknown or already in the requested state.", () => training.SetLinkConnected(id, connected));

    public CommandResult SetLinkImpairment(string id, double latency, double loss, Actor actor) =>
        Execute(id, actor, "Link impairment updated.", "Link is unknown or impairment is out of range.",
            () => training.SetLinkImpairment(id, latency, loss));

    private CommandResult Execute(string id, Actor actor, string success, string failure, Func<bool> run)
    {
        if (!actor.HasRole(AccessRole.Administrator))
        {
            const string message = "Administrator role required.";
            events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.AuthorizationDenied, EventSeverity.Medium,
                "instructor-training", message, actor.Name, id));
            return CommandResult.Rejected(id, CommandRejectionReason.NotAuthorized, message);
        }

        return run() ? CommandResult.Success(id, success) : CommandResult.Rejected(id, CommandRejectionReason.InvalidValue, failure);
    }
}
