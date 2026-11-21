using Protocols.Iec104;
using Xunit;

namespace UnitTests.Protocols.Iec104;

public sealed class Cp56Time2aTests
{
    [Fact]
    public Task EncodesKnownVector()
    {
        // 2026-01-15 08:30:45.123 UTC, a Thursday (day of week 4).
        var timestamp = new DateTimeOffset(2026, 1, 15, 8, 30, 45, 123, TimeSpan.Zero);
        Assert.Equal(new byte[] { 0x43, 0xB0, 0x3C, 0x08, 0x0F, 0x41, 0x1A }, Cp56Time2a.Encode(timestamp));
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(2026, 1, 1, 0, 0, 0, 0)]
    [InlineData(2026, 12, 31, 23, 59, 59, 999)]
    [InlineData(2000, 2, 29, 12, 30, 15, 500)]
    [InlineData(2099, 6, 15, 6, 45, 30, 250)]
    public Task RoundTrips(int year, int month, int day, int hour, int minute, int second, int millisecond)
    {
        var timestamp = new DateTimeOffset(year, month, day, hour, minute, second, millisecond, TimeSpan.Zero);
        var decoded = Cp56Time2a.Decode(Cp56Time2a.Encode(timestamp));
        Assert.Equal(timestamp, decoded);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x01, 0x01, 0x00 })] // milliseconds 65535 > 59999
    [InlineData(new byte[] { 0x00, 0x00, 0x7E, 0x00, 0x01, 0x01, 0x00 })] // minutes 63 > 59
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x18, 0x01, 0x01, 0x00 })] // hours 24 > 23
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00 })] // day 0
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x20, 0x0D, 0x00 })] // month 13
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x08, 0x00 })] // day of week 8
    [InlineData(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x01, 0x01, 0x00 })] // invalid flag set
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x1F, 0x02, 0x00 })] // 2026-02-31 does not exist
    public Task RejectsInvalidTimeTags(byte[] data)
    {
        Assert.False(Cp56Time2a.TryDecode(data, out _));
        Assert.Throws<FormatException>(() => Cp56Time2a.Decode(data));
        return Task.CompletedTask;
    }

    [Fact]
    public Task RejectsShortInput()
    {
        Assert.False(Cp56Time2a.TryDecode(new byte[6], out _));
        return Task.CompletedTask;
    }
}
