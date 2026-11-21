namespace Protocols.Modbus;

/// <summary>Modbus TCP application header. <see cref="Length"/> counts the unit id plus the PDU.</summary>
public sealed record MbapHeader(ushort TransactionId, ushort ProtocolId, ushort Length, byte UnitId)
{
    public const int Size = 7;
    public const int MinLength = 2;
    public const int MaxLength = 254;

    public static MbapHeader? TryParse(ReadOnlySpan<byte> header)
    {
        if (header.Length < Size)
            return null;

        var parsed = new MbapHeader((ushort)(header[0] << 8 | header[1]), (ushort)(header[2] << 8 | header[3]),
            (ushort)(header[4] << 8 | header[5]), header[6]);
        return parsed.IsValid ? parsed : null;
    }

    /// <summary>Protocol id must be 0 (Modbus) and the length must allow a function code within one ADU.</summary>
    public bool IsValid => ProtocolId == 0 && Length is >= MinLength and <= MaxLength;

    public int PduLength => Length - 1;
}
