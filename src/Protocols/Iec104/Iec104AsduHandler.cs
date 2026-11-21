using Application.Events;
using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;

namespace Protocols.Iec104;

/// <summary>
/// Processes one received ASDU and produces the response ASDUs (sent as I-frames): general interrogation per common
/// address, clock synchronisation acknowledgement, double commands and regulating step commands mapped to
/// <see cref="IGridControl"/>. Unknown type / common address / IOA get a negative confirmation with COT 44-47; command
/// values that the standard does not permit (RCO/RCS 0 or 3, DCO 3, select with DCO 0) get a negative activation
/// confirmation (COT 8). The address map is re-synchronised from <see cref="IGridView.Definition"/> on every ASDU, so a
/// rebuilt map is used immediately. Commands are executed under a per-connection actor name (see
/// <see cref="Iec104Connection.CommandActorName"/>) so select-before-operate ownership stays per master.
/// </summary>
public sealed class Iec104AsduHandler(
    Iec104AddressMapProvider maps,
    IGridView view,
    IGridControl control,
    Iec104Options options,
    IEventPublisher events,
    TimeProvider time)
{
    private const int GeneralInterrogation = 20;

    public async Task<IReadOnlyList<Asdu>> HandleAsync(Asdu asdu, string source, string commandActor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asdu);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandActor);
        // Pick up a rebuilt map immediately when the active grid definition changed (version activation).
        var map = maps.Sync(view.Definition);
        var header = asdu.Header;
        if (map.Rtu(header.CommonAddress) is null)
            return [Echo(asdu, CauseOfTransmission.UnknownCommonAddress)];

        return header.Type switch
        {
            Iec104TypeId.C_IC_NA_1 => HandleInterrogation(asdu, map),
            Iec104TypeId.C_CS_NA_1 => HandleClockSync(asdu, source),
            Iec104TypeId.C_DC_NA_1 or Iec104TypeId.C_DC_TA_1 => await HandleDoubleCommandAsync(asdu, map, source, commandActor, cancellationToken),
            Iec104TypeId.C_RC_NA_1 => await HandleStepCommandAsync(asdu, map, source, commandActor, cancellationToken),
            _ => [Echo(asdu, CauseOfTransmission.UnknownTypeIdentification)]
        };
    }

    private IReadOnlyList<Asdu> HandleInterrogation(Asdu asdu, Iec104AddressMap map)
    {
        var responses = new List<Asdu>();
        var qoi = asdu.Objects.Count > 0 && asdu.Objects[0].Data.Length > 0 ? asdu.Objects[0].Data[0] & 0x7F : 0;
        if (qoi != GeneralInterrogation)
        {
            responses.Add(Echo(asdu, CauseOfTransmission.ActivationConfirmNegative));
            return responses;
        }

        responses.Add(Echo(asdu, CauseOfTransmission.ActivationConfirm));
        // A master interrogates the operator view: reported values, never true values, spoofing state or injected faults.
        var snapshot = GridViewRedactor.ForOperators(view.Snapshot(), view.Definition);
        var now = time.GetUtcNow();
        var packType = (Iec104TypeId?)null;
        var objects = new List<Iec104InformationObject>();
        var payloadSize = 0;
        foreach (var point in map.Rtu(asdu.Header.CommonAddress)!.Points)
        {
            if (Iec104PointEvaluator.Evaluate(point, snapshot) is not { } sample)
                continue;
            var (type, data) = Iec104SampleEncoder.Encode(sample, withTimeTag: false, now);
            var objectSize = data.Length + Iec104InformationObject.AddressSize;
            if (packType is { } current && (current != type || payloadSize + objectSize > Asdu.MaxPayload))
            {
                responses.Add(Asdu.Build(asdu.Header with { Type = current, Cause = CauseOfTransmission.InterrogationResponse, SingleStructure = false }, objects));
                objects = [];
                payloadSize = 0;
            }

            packType = type;
            objects.Add(new Iec104InformationObject(point.InformationObjectAddress, data));
            payloadSize += objectSize;
        }

        if (objects.Count > 0 && packType is { } lastType)
            responses.Add(Asdu.Build(asdu.Header with { Type = lastType, Cause = CauseOfTransmission.InterrogationResponse, SingleStructure = false }, objects));
        responses.Add(Echo(asdu, CauseOfTransmission.ActivationTermination));
        return responses;
    }

    private IReadOnlyList<Asdu> HandleClockSync(Asdu asdu, string source)
    {
        // Acknowledge only: the slave never changes its own clock from a client time tag.
        Audit(source, $"IEC 104 clock synchronisation acknowledged for CA {asdu.Header.CommonAddress}; server time unchanged.",
            EventSeverity.Information, EventCategories.Protocol, options.CommandActor);
        return [Echo(asdu, CauseOfTransmission.ActivationConfirm)];
    }

    private async Task<IReadOnlyList<Asdu>> HandleDoubleCommandAsync(Asdu asdu, Iec104AddressMap map, string source,
        string commandActor, CancellationToken cancellationToken)
    {
        var responses = new List<Asdu>();
        var anyAccepted = false;
        var anyExecuted = false;
        foreach (var item in asdu.Objects)
        {
            // DCO (IEC 60870-5-101/104): bit 7 S/E, bits 0-1: 1 = open, 2 = close, 3 not permitted;
            // bits 0-1 = 0 with S/E=0 is the deactivation (cancel) of a previous selection.
            var (select, state) = item.Data.Length > 0
                ? Iec104ElementCodec.DecodeControlByte(item.Data[0])
                : (false, 0);
            var commandType = (select, state) switch
            {
                (true, 1) => GridCommandType.SelectOpen,
                (true, 2) => GridCommandType.SelectClose,
                (false, 1) or (false, 2) => GridCommandType.Execute,
                (false, 0) => GridCommandType.Cancel,
                _ => (GridCommandType?)null // (select, 0), (select, 3), (execute, 3): not permitted
            };
            anyExecuted |= commandType is GridCommandType.Execute or GridCommandType.Cancel;
            var cot = await ExecuteCommandAsync(asdu, map, item, commandType, operate: true, source, commandActor, cancellationToken);
            anyAccepted |= cot == CauseOfTransmission.ActivationConfirm;
            responses.Add(Asdu.Build(asdu.Header with { Cause = cot, SingleStructure = false }, [item]));
        }

        // Select-before-operate: a select is confirmed without termination; execute/cancel end with the act term.
        if (anyAccepted && anyExecuted)
            responses.Add(Echo(asdu, CauseOfTransmission.ActivationTermination));
        return responses;
    }

    private async Task<IReadOnlyList<Asdu>> HandleStepCommandAsync(Asdu asdu, Iec104AddressMap map, string source,
        string commandActor, CancellationToken cancellationToken)
    {
        var responses = new List<Asdu>();
        var anyAccepted = false;
        var anyExecuted = false;
        foreach (var item in asdu.Objects)
        {
            // RCC (IEC 60870-5-101/104): bit 7 S/E, bits 0-1: 0 and 3 not permitted, 1 = next step LOWER, 2 = next step HIGHER.
            var (select, state) = item.Data.Length > 0
                ? Iec104ElementCodec.DecodeControlByte(item.Data[0])
                : (false, 0);
            var commandType = state switch
            {
                1 => GridCommandType.TapLower,
                2 => GridCommandType.TapRaise,
                _ => (GridCommandType?)null // RCO/RCC 0 or 3: not permitted
            };
            anyExecuted |= !select && commandType is not null;
            // S/E=1 (select) is confirmed without operating; only S/E=0 (execute) steps the tap.
            var cot = await ExecuteCommandAsync(asdu, map, item, commandType, operate: !select, source, commandActor, cancellationToken);
            anyAccepted |= cot == CauseOfTransmission.ActivationConfirm;
            responses.Add(Asdu.Build(asdu.Header with { Cause = cot, SingleStructure = false }, [item]));
        }

        // Same select-before-operate flow as double commands: select → act con only; execute → act con + act term.
        if (anyAccepted && anyExecuted)
            responses.Add(Echo(asdu, CauseOfTransmission.ActivationTermination));
        return responses;
    }

    private async Task<CauseOfTransmission> ExecuteCommandAsync(Asdu asdu, Iec104AddressMap map, Iec104InformationObject item,
        GridCommandType? commandType, bool operate, string source, string commandActor, CancellationToken cancellationToken)
    {
        var ca = asdu.Header.CommonAddress;
        var ioa = item.InformationObjectAddress;
        if (!options.CommandsEnabled)
        {
            Audit(source, $"IEC 104 command for CA {ca} IOA {ioa} rejected: commands are disabled.",
                EventSeverity.High, EventCategories.CommandRejected, commandActor);
            return CauseOfTransmission.ActivationConfirmNegative;
        }

        var point = map.Point(ca, ioa);
        if (point is null || !point.Controllable)
        {
            Audit(source, $"IEC 104 command for CA {ca} IOA {ioa} rejected: unknown or non-commandable object.",
                EventSeverity.Medium, EventCategories.CommandRejected, commandActor);
            return CauseOfTransmission.UnknownInformationObjectAddress;
        }

        if (commandType is null)
        {
            Audit(source, $"IEC 104 command for CA {ca} IOA {ioa} ({point.ElementId}) rejected: command value not permitted " +
                "(RCO/RCC 0 or 3, DCO 3, select with DCO 0).", EventSeverity.Medium, EventCategories.CommandRejected, commandActor);
            return CauseOfTransmission.ActivationConfirmNegative;
        }

        if (!operate)
        {
            // S/E=1 (select): confirm the selection only; no operation happens until an execute (S/E=0) arrives.
            Audit(source, $"IEC 104 selection {commandType} on {point.ElementId} (CA {ca} IOA {ioa}) confirmed; no operation until execute.",
                EventSeverity.Information, EventCategories.Protocol, commandActor);
            return CauseOfTransmission.ActivationConfirm;
        }

        var command = new GridCommand(commandType.Value, point.ElementId);
        var actor = new Actor(commandActor, options.CommandRole);
        var result = await control.ExecuteAsync(command, actor, "iec104", cancellationToken);
        Audit(source, $"IEC 104 command {commandType} on {point.ElementId} (CA {ca} IOA {ioa}) " +
            $"{(result.Accepted ? "accepted" : "rejected")}: {result.Message}",
            result.Accepted ? EventSeverity.Medium : EventSeverity.High,
            result.Accepted ? EventCategories.Protocol : EventCategories.CommandRejected, commandActor);
        return result.Accepted ? CauseOfTransmission.ActivationConfirm : CauseOfTransmission.ActivationConfirmNegative;
    }

    private static Asdu Echo(Asdu asdu, CauseOfTransmission cause) =>
        asdu.Objects.Count > 0
            ? Asdu.Build(asdu.Header with { Cause = cause, SingleStructure = false }, asdu.Objects)
            : new Asdu(asdu.Header with { Cause = cause }, asdu.Payload, asdu.Objects);

    private bool Audit(string source, string message, EventSeverity severity, string category, string actor) =>
        events.Publish(SecurityEvent.Create(time.GetUtcNow(), category, severity, source, message, actor));
}
