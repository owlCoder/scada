namespace Protocols.Iec104;

/// <summary>
/// The 4-octet application protocol control information (APCI). Sequence numbers are 15-bit and wrap at 32767.
/// Control-field 16-bit words are little-endian; the two low bits of the first word select the frame kind.
/// </summary>
public readonly record struct Apci(ApduFrameKind Kind, int SendSequence, int ReceiveSequence, byte UnnumberedFunction)
{
    public const int Size = 4;
    public const int MaxSequence = 0x7FFF;

    // U-format functions (control octet values, format bits 0x03 included).
    public const byte StartDtAct = 0x07;
    public const byte StartDtCon = 0x0B;
    public const byte StopDtAct = 0x13;
    public const byte StopDtCon = 0x23;
    public const byte TestFrAct = 0x43;
    public const byte TestFrCon = 0x83;

    public static Apci Information(int sendSequence, int receiveSequence) =>
        new(ApduFrameKind.Information, Mask(sendSequence), Mask(receiveSequence), 0);

    public static Apci Supervisory(int receiveSequence) =>
        new(ApduFrameKind.Supervisory, 0, Mask(receiveSequence), 0x01);

    public static Apci Unnumbered(byte function) =>
        new(ApduFrameKind.Unnumbered, 0, 0, function);

    /// <summary>The next 15-bit sequence number, wrapping 32767 back to 0.</summary>
    public static int Next(int sequence) => (Mask(sequence) + 1) & MaxSequence;

    /// <summary>Distance from <paramref name="from"/> to <paramref name="to"/> modulo 32768 (how many sequence numbers ahead).</summary>
    public static int Distance(int from, int to) => (Mask(to) - Mask(from)) & MaxSequence;

    public static Apci? TryParse(ReadOnlySpan<byte> control)
    {
        if (control.Length < Size)
            return null;
        var first = control[0] | control[1] << 8;
        var second = control[2] | control[3] << 8;
        // Bit 0 of the first octet selects I (0) vs S/U (1); bit 1 separates S (0) from U (1). I-frames carry both
        // sequence numbers in bits 1-15 of each 16-bit word; S-frames carry only the receive sequence in the second word.
        if ((first & 0x01) == 0)
            return new Apci(ApduFrameKind.Information, (first >> 1) & MaxSequence, (second >> 1) & MaxSequence, 0);
        if ((first & 0x03) == 0x01)
            return new Apci(ApduFrameKind.Supervisory, 0, (second >> 1) & MaxSequence, 0x01);
        if ((first & 0x03) == 0x03)
            return new Apci(ApduFrameKind.Unnumbered, 0, 0, control[0]);
        return null;
    }

    public int WriteTo(Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, Size);
        int first;
        int second;
        switch (Kind)
        {
            case ApduFrameKind.Information:
                first = Mask(SendSequence) << 1;
                second = Mask(ReceiveSequence) << 1;
                break;
            case ApduFrameKind.Supervisory:
                first = 0x01;
                second = Mask(ReceiveSequence) << 1;
                break;
            case ApduFrameKind.Unnumbered:
                first = UnnumberedFunction | 0x03;
                second = 0;
                break;
            default:
                throw new InvalidOperationException($"Unknown frame kind {Kind}.");
        }

        destination[0] = (byte)first;
        destination[1] = (byte)(first >> 8);
        destination[2] = (byte)second;
        destination[3] = (byte)(second >> 8);
        return Size;
    }

    public byte[] Serialize()
    {
        var bytes = new byte[Size];
        WriteTo(bytes);
        return bytes;
    }

    private static int Mask(int sequence) => sequence & MaxSequence;
}
