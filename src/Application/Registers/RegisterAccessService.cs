using Application.Events;
using Application.Identity;
using Application.Simulation;
using Domain.Control;

namespace Application.Registers;

/// <summary>
/// Protocol-neutral access to controller register images. Unit identifiers are 1-based positions of the
/// configured controllers. Denied and invalid operations are audited because they are attack indicators.
/// </summary>
public sealed class RegisterAccessService(
    SimulationEngine engine,
    RegisterWriteOptions options,
    IEventPublisher events,
    TimeProvider time)
{
    public IReadOnlyList<ControllerRegisterView> ListControllers() => engine.Read(world =>
        world.Controllers.Select((c, i) => new ControllerRegisterView((byte)(i + 1), c.Id, c.Definition.Name,
            c.Definition.Kind, c.Snapshot().Registers)).ToArray());

    public string? ControllerForUnit(byte unitId) => engine.Read(world =>
        unitId >= 1 && unitId <= world.Controllers.Count ? world.Controllers[unitId - 1].Id : null);

    public RegisterReadResult Read(byte unitId, RegisterArea area, ushort start, ushort count, string source)
    {
        var result = engine.Read(world =>
        {
            if (unitId < 1 || unitId > world.Controllers.Count)
                return new RegisterReadResult(RegisterOperationStatus.UnknownUnit, [], $"Unknown unit {unitId}.");
            if (count is < 1 or > 125)
                return new RegisterReadResult(RegisterOperationStatus.IllegalValue, [], "Register count must be 1 to 125.");
            if (start + count > ControllerRegisterMap.Size)
                return new RegisterReadResult(RegisterOperationStatus.IllegalAddress, [],
                    $"Registers {start}..{start + count - 1} are outside the {ControllerRegisterMap.Size}-register map.");

            var controller = world.Controllers[unitId - 1];
            var values = Enumerable.Range(start, count).Select(a => controller.Registers.Read(area, (ushort)a)).ToArray();
            return new RegisterReadResult(RegisterOperationStatus.Success, values, "OK");
        });

        if (result.Status != RegisterOperationStatus.Success)
            Audit(EventCategories.Protocol, EventSeverity.Low, source, $"Invalid register read on unit {unitId}: {result.Message}",
                source, ControllerForUnit(unitId));
        return result;
    }

    public RegisterWriteResult WriteSetpoint(byte unitId, ushort address, ushort value, Actor? actor, string source)
    {
        var controllerId = ControllerForUnit(unitId);
        var actorName = actor?.Name ?? "anonymous";
        if (controllerId is null)
            return Fail(RegisterOperationStatus.UnknownUnit, $"Unknown unit {unitId}.", source, actorName, null, EventCategories.Protocol);
        if (!options.WritesEnabled)
            return Fail(RegisterOperationStatus.Denied, "Register writes are disabled outside the training environment.",
                source, actorName, controllerId, EventCategories.Protocol);
        if (actor is null || !actor.HasRole(AccessRole.Operator))
            return Fail(RegisterOperationStatus.Denied, $"Unauthorised register write to {controllerId}:{address}.",
                source, actorName, controllerId, EventCategories.AuthorizationDenied);
        if (address is < ControllerRegisterMap.SetpointBase or >= ControllerRegisterMap.StatusBase)
            return Fail(RegisterOperationStatus.IllegalAddress, $"Holding register {address} on {controllerId} is not writable.",
                source, actorName, controllerId, EventCategories.Protocol);
        if (value > options.MaxSetpointValue)
            return Fail(RegisterOperationStatus.IllegalValue, $"Value {value} exceeds the setpoint limit {options.MaxSetpointValue}.",
                source, actorName, controllerId, EventCategories.Protocol);

        var previous = engine.Mutate(world =>
        {
            var controller = world.FindController(controllerId)!;
            var old = controller.Registers.Read(RegisterArea.Holding, address);
            return controller.WriteSetpoint(address, value) ? (ushort?)old : null;
        });
        if (previous is null)
            return Fail(RegisterOperationStatus.IllegalAddress, "Setpoint write failed.", source, actorName, controllerId, EventCategories.Protocol);

        Audit(EventCategories.Audit, EventSeverity.Medium, source,
            $"Setpoint {controllerId}:HR{address} changed {previous} -> {value}", actorName, controllerId);
        return new RegisterWriteResult(RegisterOperationStatus.Success, "OK");
    }

    private RegisterWriteResult Fail(RegisterOperationStatus status, string message, string source, string actor,
        string? controllerId, string category)
    {
        Audit(category, status == RegisterOperationStatus.Denied ? EventSeverity.High : EventSeverity.Medium, source,
            message, actor, controllerId);
        return new RegisterWriteResult(status, message);
    }

    private bool Audit(string category, EventSeverity severity, string source, string message, string actor, string? deviceId) =>
        events.Publish(SecurityEvent.Create(time.GetUtcNow(), category, severity, source, message, actor, deviceId));
}
