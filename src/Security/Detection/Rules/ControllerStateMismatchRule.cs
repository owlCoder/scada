using System.Globalization;
using Application.Detection;
using Domain.Control;
using Domain.Incidents;
using Domain.Process.Devices;

namespace Security.Detection.Rules;

/// <summary>
/// Suspicious controller and device states: controllers in safe state or faulted, active register forces,
/// and controller output registers that disagree with the actual actuator state for <c>Threshold</c> steps.
/// </summary>
public sealed class ControllerStateMismatchRule(IReadOnlyList<ControllerDefinition> controllers) : DetectionRuleBase
{
    private readonly Dictionary<string, int> _mismatchSteps = new(StringComparer.Ordinal);

    public override string Id => "controller-state-mismatch";
    public override string Name => "Suspicious controller or device state";
    public override string Description => "Controller outputs disagreeing with actuators, active forces, safe state or faults.";
    public override IncidentCategory Category => IncidentCategory.Cybersecurity;
    public override DetectionRuleSettings DefaultSettings => Settings(DetectionSeverity.High, 6, 60, 300);

    public override IReadOnlyList<DetectionFinding> Evaluate(DetectionContext context, DetectionRuleSettings settings)
    {
        var findings = new List<DetectionFinding>();
        foreach (var snapshot in context.Report.Controllers)
        {
            if (snapshot.State is ControllerState.SafeState or ControllerState.Faulted)
                findings.Add(new($"{snapshot.Id}:{snapshot.State}", $"{snapshot.Id} in {snapshot.State}",
                    $"{snapshot.Name} is in {snapshot.State}: {snapshot.LastError ?? "no detail"}.", snapshot.Id,
                    [Evidence("controller", snapshot.Id, $"State {snapshot.State}")]));

            if (snapshot.ForcedRegisterCount > 0)
                findings.Add(new($"{snapshot.Id}:forced", $"Forced registers on {snapshot.Id}",
                    $"{snapshot.ForcedRegisterCount} register(s) on {snapshot.Id} are forced and override live values.", snapshot.Id,
                    snapshot.Registers.Where(r => r.IsForced).Select(r => Evidence("register", $"{snapshot.Id}:{r.Area}:{r.Address}", $"{r.Tag} = {r.Value}")).ToArray()));

            var definition = controllers.FirstOrDefault(c => c.Id == snapshot.Id);
            if (definition is null)
                continue;

            foreach (var output in definition.Outputs)
            {
                var key = $"{snapshot.Id}:{output.DeviceId}";
                var register = snapshot.Registers.FirstOrDefault(r => r.Area == RegisterArea.Holding && r.Address == output.Address);
                var actual = Actual(context, output);
                if (register is null || actual is null || snapshot.State != ControllerState.Running ||
                    snapshot.FieldCommunication != CommunicationState.Online)
                {
                    _mismatchSteps.Remove(key);
                    continue;
                }

                var commanded = register.Value / output.Scale;
                var steps = Math.Abs(commanded - actual.Value) > 0.5 ? _mismatchSteps.GetValueOrDefault(key) + 1 : 0;
                _mismatchSteps[key] = steps;
                if (steps == (int)Math.Max(1, settings.Threshold))
                    findings.Add(new(key, $"{snapshot.Id} output disagrees with {output.DeviceId}",
                        string.Create(CultureInfo.InvariantCulture, $"{snapshot.Id} register {output.Address} ({output.Tag}) holds {commanded:0.#} but {output.DeviceId} is at {actual:0.#}."),
                        output.DeviceId,
                        [Evidence("register", $"{snapshot.Id}:HR{output.Address}", string.Create(CultureInfo.InvariantCulture, $"{commanded:0.#}")),
                         Evidence("device", output.DeviceId, string.Create(CultureInfo.InvariantCulture, $"{actual:0.#}"))]));
            }
        }

        return findings;
    }

    private static double? Actual(DetectionContext context, IoPoint output) => output.Signal switch
    {
        IoSignal.PumpRunCommand => context.Current.Pump(output.DeviceId) is { } pump
            ? pump.State is PumpState.Starting or PumpState.Running ? 1 : 0
            : null,
        IoSignal.ValvePositionCommand => context.Current.Valve(output.DeviceId)?.Setpoint,
        _ => null
    };
}
