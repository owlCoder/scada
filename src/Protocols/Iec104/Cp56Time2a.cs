namespace Protocols.Iec104;

/// <summary>
/// CP56Time2a, the 7-octet time tag: milliseconds (bits 0-15), invalid flag (bit 16), minutes (17-22), summer time
/// (bit 23), hours (24-28), day of month (32-37), month (40-43), day of week (44-47, Monday=1), year 0-99 (48-54).
/// </summary>
public static class Cp56Time2a
{
    public const int Size = 7;

    public static byte[] Encode(DateTimeOffset timestamp)
    {
        var local = timestamp.UtcDateTime;
        var milliseconds = local.Second * 1000 + local.Millisecond;
        var dayOfWeek = ((int)local.DayOfWeek + 6) % 7 + 1; // Monday = 1 .. Sunday = 7
        return
        [
            (byte)milliseconds,
            (byte)(milliseconds >> 8),
            (byte)(local.Minute << 1),
            (byte)local.Hour,
            (byte)local.Day,
            (byte)((dayOfWeek << 4) | local.Month),
            (byte)(local.Year % 100)
        ];
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (data.Length < Size)
            return false;
        var milliseconds = data[0] | data[1] << 8;
        var invalid = (data[2] & 0x01) != 0;
        var minutes = (data[2] >> 1) & 0x3F;
        var hours = data[3] & 0x1F;
        var day = data[4] & 0x1F;
        var month = data[5] & 0x0F;
        var dayOfWeek = data[5] >> 4;
        var year = data[6] & 0x7F;
        if (milliseconds > 59_999 || minutes > 59 || hours > 23 || day is < 1 or > 31 || month is < 1 or > 12
            || dayOfWeek is < 1 or > 7)
            return false;
        try
        {
            timestamp = new DateTimeOffset(2000 + year, month, day, hours, minutes, milliseconds / 1000,
                milliseconds % 1000, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        return !invalid;
    }

    public static DateTimeOffset Decode(ReadOnlySpan<byte> data) =>
        TryDecode(data, out var timestamp) ? timestamp : throw new FormatException("Invalid CP56Time2a time tag.");
}
