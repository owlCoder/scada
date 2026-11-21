namespace Protocols.Iec104;

/// <summary>
/// The 6-octet ASDU header: type identification, SQ bit + element count, cause of transmission, originator address
/// (1 octet) and common address of the RTU (2 octets, little-endian).
/// </summary>
public readonly record struct AsduHeader(
    Iec104TypeId Type,
    bool SingleStructure,
    int ElementCount,
    CauseOfTransmission Cause,
    int OriginatorAddress,
    int CommonAddress)
{
    public const int Size = 6;
    public const int MaxElements = 127;
    public const int MaxCommonAddress = 0xFFFF;

    public static AsduHeader? TryParse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
            return null;
        var type = (Iec104TypeId)data[0];
        var count = data[1] & 0x7F;
        if (count is < 1 or > MaxElements)
            return null;
        return new AsduHeader(type, (data[1] & 0x80) != 0, count, (CauseOfTransmission)data[2], data[3], data[4] | data[5] << 8);
    }

    public int WriteTo(Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, Size);
        if (ElementCount is < 1 or > MaxElements)
            throw new ArgumentOutOfRangeException(nameof(ElementCount));
        if (CommonAddress is < 0 or > MaxCommonAddress)
            throw new ArgumentOutOfRangeException(nameof(CommonAddress));
        destination[0] = (byte)Type;
        destination[1] = (byte)(ElementCount | (SingleStructure ? 0x80 : 0));
        destination[2] = (byte)Cause;
        destination[3] = (byte)OriginatorAddress;
        destination[4] = (byte)CommonAddress;
        destination[5] = (byte)(CommonAddress >> 8);
        return Size;
    }

    public byte[] Serialize()
    {
        var bytes = new byte[Size];
        WriteTo(bytes);
        return bytes;
    }
}
