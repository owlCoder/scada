namespace Protocols;

public sealed class ModbusFrameService(VirtualRegisterService registers)
{
    public byte[] Process(ReadOnlySpan<byte> request)
    {
        if (request.Length < 1)
            return [0x83, 0x03];

        var function = request[0];
        if (function != 3)
            return [(byte)(function | 0x80), 0x01];

        if (request.Length != 5)
            return [0x83, 0x03];

        var start = (request[1] << 8) | request[2];
        var count = (request[3] << 8) | request[4];

        if (count is < 1 or > 125)
            return [0x83, 0x03];

        if (start + count > 65536)
            return [0x83, 0x02];

        var result = new byte[2 + count * 2];
        result[0] = 3;
        result[1] = (byte)(count * 2);

        for (var i = 0; i < count; i++)
        {
            var register = registers.Read((ushort)(start + i));
            if (register is null)
                return [0x83, 0x02];

            result[2 + i * 2] = (byte)(register.Value >> 8);
            result[3 + i * 2] = (byte)register.Value;
        }

        return result;
    }
}
