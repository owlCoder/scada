namespace Protocols.Iec104;

/// <summary>One information object: a 3-octet little-endian information object address plus its type-specific data.</summary>
public sealed record Iec104InformationObject(int InformationObjectAddress, byte[] Data)
{
    public const int AddressSize = 3;

    public static int WriteAddressTo(int ioa, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, AddressSize);
        if (ioa is < 0 or > 0xFFFFFF)
            throw new ArgumentOutOfRangeException(nameof(ioa));
        destination[0] = (byte)ioa;
        destination[1] = (byte)(ioa >> 8);
        destination[2] = (byte)(ioa >> 16);
        return AddressSize;
    }

    public static int ReadAddress(ReadOnlySpan<byte> data) => data[0] | data[1] << 8 | data[2] << 16;
}
