namespace Protocols.Iec104;

/// <summary>
/// One IEC 60870-5-104 APDU: start byte 0x68, a length octet counting the bytes that follow (APCI + ASDU, so 4..253),
/// the 4-octet APCI and the ASDU payload.
/// </summary>
public readonly record struct Apdu(Apci Control, byte[] Payload)
{
    public const byte StartByte = 0x68;
    public const int HeaderSize = 2;
    public const int MinLength = Apci.Size;
    public const int MaxLength = 253;
    public const int MaxTotalSize = HeaderSize + MaxLength;

    public static Apdu? TryParse(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < HeaderSize + MinLength)
            return null;
        if (frame[0] != StartByte)
            return null;
        var length = frame[1];
        if (length is < MinLength or > MaxLength || frame.Length != HeaderSize + length)
            return null;
        var control = Apci.TryParse(frame.Slice(HeaderSize, Apci.Size));
        if (control is null)
            return null;
        return new Apdu(control.Value, frame.Slice(HeaderSize + Apci.Size).ToArray());
    }

    public byte[] Serialize()
    {
        ArgumentNullException.ThrowIfNull(Payload);
        if (Payload.Length > MaxLength - Apci.Size)
            throw new ArgumentOutOfRangeException(nameof(Payload), "ASDU payload does not fit into one APDU.");
        var frame = new byte[HeaderSize + Apci.Size + Payload.Length];
        frame[0] = StartByte;
        frame[1] = (byte)(Apci.Size + Payload.Length);
        Control.WriteTo(frame.AsSpan(HeaderSize, Apci.Size));
        Payload.CopyTo(frame, HeaderSize + Apci.Size);
        return frame;
    }
}
