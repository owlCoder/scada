using Application.Events;
using Application.Identity;
using Application.Registers;
using Domain.Control;

namespace Protocols.Modbus;

/// <summary>
/// Protocol logic for Modbus TCP over the controller register images. Unit ids address controllers (1-based).
/// Reads use FC03/FC04; FC06/FC16 setpoint writes are only served when register writes are enabled.
/// </summary>
public sealed class ModbusRequestProcessor(
    RegisterAccessService registers,
    RegisterWriteOptions writeOptions,
    ModbusOptions options,
    IEventPublisher events,
    TimeProvider time)
{
    private const int MaxReadCount = 125;
    private const int MaxWriteCount = 123;

    /// <summary>Processes one complete ADU. Returns null when the MBAP header is invalid and the connection must be closed.</summary>
    public byte[]? Process(ReadOnlySpan<byte> adu, string source)
    {
        var header = MbapHeader.TryParse(adu);
        if (header is null || adu.Length != MbapHeader.Size + header.PduLength)
        {
            Audit(source, null, "Malformed Modbus frame (invalid MBAP header or length)");
            return null;
        }

        var pdu = ProcessPdu(header.UnitId, adu[MbapHeader.Size..], source);
        var response = new byte[MbapHeader.Size + pdu.Length];
        response[0] = (byte)(header.TransactionId >> 8);
        response[1] = (byte)header.TransactionId;
        response[4] = (byte)((pdu.Length + 1) >> 8);
        response[5] = (byte)(pdu.Length + 1);
        response[6] = header.UnitId;
        pdu.CopyTo(response, MbapHeader.Size);
        return response;
    }

    public byte[] ProcessPdu(byte unitId, ReadOnlySpan<byte> pdu, string source)
    {
        if (pdu.Length == 0)
            return Exception(0, ModbusExceptionCode.IllegalDataValue);

        var function = pdu[0];
        if (registers.ControllerForUnit(unitId) is null)
        {
            Audit(source, null, $"Modbus request for unknown unit {unitId}");
            return Exception(function, ModbusExceptionCode.GatewayTargetDeviceFailedToRespond);
        }

        return (ModbusFunctionCode)function switch
        {
            ModbusFunctionCode.ReadHoldingRegisters => Read(unitId, RegisterArea.Holding, pdu, source),
            ModbusFunctionCode.ReadInputRegisters => Read(unitId, RegisterArea.Input, pdu, source),
            ModbusFunctionCode.WriteSingleRegister when writeOptions.WritesEnabled => WriteSingle(unitId, pdu, source),
            ModbusFunctionCode.WriteMultipleRegisters when writeOptions.WritesEnabled => WriteMultiple(unitId, pdu, source),
            _ => IllegalFunction(unitId, function, source)
        };
    }

    private byte[] Read(byte unitId, RegisterArea area, ReadOnlySpan<byte> pdu, string source)
    {
        var function = pdu[0];
        if (pdu.Length != 5)
            return Exception(function, ModbusExceptionCode.IllegalDataValue);

        var start = Word(pdu, 1);
        var count = Word(pdu, 3);
        if (count is < 1 or > MaxReadCount)
            return Exception(function, ModbusExceptionCode.IllegalDataValue);

        var result = registers.Read(unitId, area, start, count, source);
        if (result.Status != RegisterOperationStatus.Success)
            return Exception(function, Map(result.Status));

        var response = new byte[2 + result.Values.Count * 2];
        response[0] = function;
        response[1] = (byte)(result.Values.Count * 2);
        for (var i = 0; i < result.Values.Count; i++)
        {
            response[2 + i * 2] = (byte)(result.Values[i] >> 8);
            response[3 + i * 2] = (byte)result.Values[i];
        }

        return response;
    }

    private byte[] WriteSingle(byte unitId, ReadOnlySpan<byte> pdu, string source)
    {
        var function = pdu[0];
        if (pdu.Length != 5)
            return Exception(function, ModbusExceptionCode.IllegalDataValue);

        var result = registers.WriteSetpoint(unitId, Word(pdu, 1), Word(pdu, 3), WriteActor, source);
        return result.Status == RegisterOperationStatus.Success ? pdu.ToArray() : Exception(function, Map(result.Status));
    }

    private byte[] WriteMultiple(byte unitId, ReadOnlySpan<byte> pdu, string source)
    {
        var function = pdu[0];
        if (pdu.Length < 6)
            return Exception(function, ModbusExceptionCode.IllegalDataValue);

        var start = Word(pdu, 1);
        var count = Word(pdu, 3);
        var byteCount = pdu[5];
        if (count is < 1 or > MaxWriteCount || byteCount != count * 2 || pdu.Length != 6 + byteCount)
            return Exception(function, ModbusExceptionCode.IllegalDataValue);

        // Validate the whole request first so a rejected write never leaves a partial update behind.
        if (start < ControllerRegisterMap.SetpointBase || start + count > ControllerRegisterMap.StatusBase)
        {
            Audit(source, registers.ControllerForUnit(unitId), $"Modbus write to non-writable registers {start}..{start + count - 1} on unit {unitId}");
            return Exception(function, ModbusExceptionCode.IllegalDataAddress);
        }

        var values = new ushort[count];
        for (var i = 0; i < count; i++)
            values[i] = Word(pdu, 6 + i * 2);
        if (values.Any(v => v > writeOptions.MaxSetpointValue))
        {
            Audit(source, registers.ControllerForUnit(unitId), $"Modbus write with out-of-range value on unit {unitId}");
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        for (var i = 0; i < count; i++)
        {
            var result = registers.WriteSetpoint(unitId, (ushort)(start + i), values[i], WriteActor, source);
            if (result.Status != RegisterOperationStatus.Success)
                return Exception(function, Map(result.Status));
        }

        return [function, pdu[1], pdu[2], pdu[3], pdu[4]];
    }

    private byte[] IllegalFunction(byte unitId, byte function, string source)
    {
        Audit(source, registers.ControllerForUnit(unitId), $"Unsupported or disabled Modbus function 0x{function:X2} on unit {unitId}");
        return Exception(function, ModbusExceptionCode.IllegalFunction);
    }

    private Actor WriteActor => new(options.WriteActor, AccessRole.Operator);

    private static ModbusExceptionCode Map(RegisterOperationStatus status) => status switch
    {
        RegisterOperationStatus.IllegalAddress => ModbusExceptionCode.IllegalDataAddress,
        RegisterOperationStatus.IllegalValue => ModbusExceptionCode.IllegalDataValue,
        RegisterOperationStatus.Denied => ModbusExceptionCode.IllegalFunction,
        RegisterOperationStatus.UnknownUnit => ModbusExceptionCode.GatewayTargetDeviceFailedToRespond,
        _ => ModbusExceptionCode.ServerDeviceFailure
    };

    private static ushort Word(ReadOnlySpan<byte> data, int offset) => (ushort)(data[offset] << 8 | data[offset + 1]);

    private static byte[] Exception(byte function, ModbusExceptionCode code) => [(byte)(function | 0x80), (byte)code];

    private bool Audit(string source, string? deviceId, string message) =>
        events.Publish(SecurityEvent.Create(time.GetUtcNow(), EventCategories.Protocol, EventSeverity.Medium, source, message,
            source, deviceId));
}
