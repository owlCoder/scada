namespace Protocols.Modbus;

public enum ModbusExceptionCode : byte
{
    IllegalFunction = 0x01,
    IllegalDataAddress = 0x02,
    IllegalDataValue = 0x03,
    ServerDeviceFailure = 0x04,
    GatewayTargetDeviceFailedToRespond = 0x0B
}
