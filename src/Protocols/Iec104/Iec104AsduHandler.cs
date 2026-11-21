using Application.Events;
using Application.Grid;
using Application.Identity;
using Domain.Grid.Commands;

namespace Protocols.Iec104;

/// <summary>
/// Processes one received ASDU and produces the response ASDUs (sent as I-frames): general interrogation per common
/// address, clock synchronisation acknowledgement, double commands with select/execute and regulating step commands
/// mapped to <see cref="IGridControl"/>. Unknown type / common address / IOA get a negative confirmation with COT 44-47.
/// </summary>
public sealed class Iec104AsduHandler(
    Iec104AddressMap map,
    IGridView view,
    IGridControl control,
    Iec104Options options,
    IEventPublisher events,
    TimeProvider time)
{
    private const int GeneralInterrogation = 20;

    public async Task<IReadOnlyList<Asdu>> HandleAsync(Asdu asdu, string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asdu);
        var header = asdu.Header;
        if (map.Rtu(header.CommonAddress) is null)
            return [Echo(asdu, CauseOfTransmission.UnknownCommonAddress)];

        return header.Type switch
        {
            Iec104TypeId.C_IC_NA_1 => HandleInterrogation(asdu),
            Iec104TypeId.C_CS_NA_1 => HandleClockSync(asdu, source),
            Iec104TypeId.C_DC_NA_1 or Iec104TypeId.C_DC_TA_1 => await HandleDoubleCommandAsync(asdu, source, cancellationToken),
            Iec104TypeId.C_RC_NA_1 => await HandleStepCommandAsync(asdu, source, cancellationToken),
            _ => [Echo(asdu, CauseOfTransmission.UnknownTypeIdentification)]
        };
    }

    private IReadOnlyList<Asdu> HandleInterrogation(Asdu asdu)
    {
        var responses = new List<Asdu>();
        var qoi = asdu.Objects.Count > 0 && asdu.Objects[0].Data.Length > 0 ? asdu.Objects[0].Data[0] & 0x7F : 0;
        if (qoi != GeneralInterrogation)
        {
            responses.Add(Echo(asdu, CauseOfTransmission.ActivationConfirmNegative));
            return responses;
        }

        responses.Add(Echo(asdu, CauseOfTransmission.ActivationConfirm));
        var snapshot = view.Snapshot();
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
            EventSeverity.Information, EventCategories.Protocol);
        return [Echo(asdu, CauseOfTransmission.ActivationConfirm)];
    }

    private async Task<IReadOnlyList<Asdu>> HandleDoubleCommandAsync(Asdu asdu, string source, CancellationToken cancellationToken)
    {
        var responses = new List<Asdu>();
        var anyAccepted = false;
        var anyExecuted = false;
        foreach (var item in asdu.Objects)
        {
            var (select, state) = item.Data.Length > 0
                ? Iec104ElementCodec.DecodeControlByte(item.Data[0])
                : (false, 0);
            var commandType = (select, state) switch
            {
                (true, 1) => GridCommandType.SelectOpen,
                (true, 2) => GridCommandType.SelectClose,
                (false, 1) or (false, 2) => GridCommandType.Execute,
                (false, 0) => GridCommandType.Cancel,
                _ => (GridCommandType?)null
            };
            anyExecuted |= commandType is GridCommandType.Execute or GridCommandType.Cancel;
            var cot = await ExecuteCommandAsync(asdu, item, commandType, source, cancellationToken);
            anyAccepted |= cot == CauseOfTransmission.ActivationConfirm;
            responses.Add(Asdu.Build(asdu.Header with { Cause = cot, SingleStructure = false }, [item]));
        }

        // Select-before-operate: a select is confirmed without termination; execute/cancel end with the act term.
        if (anyAccepted && anyExecuted)
            responses.Add(Echo(asdu, CauseOfTransmission.ActivationTermination));
        return responses;
    }

    private async Task<IReadOnlyList<Asdu>> HandleStepCommandAsync(Asdu asdu, string source, CancellationToken cancellationToken)
    {
        var responses = new List<Asdu>();
        var anyAccepted = false;
        foreach (var item in asdu.Objects)
        {
            var state = item.Data.Length > 0 ? item.Data[0] & 0x03 : 0;
            var commandType = state switch
            {
                1 => GridCommandType.TapRaise,
                2 => GridCommandType.TapLower,
                _ => (GridCommandType?)null
            };
            var cot = await ExecuteCommandAsync(asdu, item, commandType, source, cancellationToken);
            anyAccepted |= cot == CauseOfTransmission.ActivationConfirm;
            responses.Add(Asdu.Build(asdu.Header with { Cause = cot, SingleStructure = false }, [item]));
        }

        if (anyAccepted)
            responses.Add(Echo(asdu, CauseOfTransmission.ActivationTermination));
        return responses;
    }

    private async Task<CauseOfTransmission> ExecuteCommandAsync(Asdu asdu, Iec104InformationObject item,
        GridCommandType? commandType, string source, CancellationToken cancellationToken)
    {
        var ca = asdu.Header.CommonAddress;
        var point = map.Point(ca, item.InformationObjectAddress);
        if (!options.CommandsEnabled)
        {
            Audit(source, $"IEC 104 command for CA {ca} IOA {item.InformationObjectAddress} rejected: commands are disabled.",
                EventSeverity.High, EventCategories.CommandRejected);
            return CauseOfTransmission.ActivationConfirmNegative;
        }

        if (point is null || !point.Controllable || commandType is null)
        {
            Audit(source, $"IEC 104 command for CA {ca} IOA {item.InformationObjectAddress} rejected: unknown or non-commandable object.",
                EventSeverity.Medium, EventCategories.CommandRejected);
            return CauseOfTransmission.UnknownInformationObjectAddress;
        }

        var command = new GridCommand(commandType.Value, point.ElementId);
        var actor = new Actor(options.CommandActor, options.CommandRole);
        var result = await control.ExecuteAsync(command, actor, "iec104", cancellationToken);
        Audit(source, $"IEC 104 command {commandType} on {point.ElementId} (CA {ca} IOA {item.InformationObjectAddress}) " +
            $"{(result.Accepted ? "accepted" : "rejected")}: {result.Message}",
            result.Accepted ? EventSeverity.Medium : EventSeverity.High,
            result.Accepted ? EventCategories.Protocol : EventCategories.CommandRejected);
        return result.Accepted ? CauseOfTransmission.ActivationConfirm : CauseOfTransmission.ActivationConfirmNegative;
    }

    private static Asdu Echo(Asdu asdu, CauseOfTransmission cause) =>
        asdu.Objects.Count > 0
            ? Asdu.Build(asdu.Header with { Cause = cause, SingleStructure = false }, asdu.Objects)
            : new Asdu(asdu.Header with { Cause = cause }, asdu.Payload, asdu.Objects);

    private bool Audit(string source, string message, EventSeverity severity, string category) =>
        events.Publish(SecurityEvent.Create(time.GetUtcNow(), category, severity, source, message, options.CommandActor));
}
