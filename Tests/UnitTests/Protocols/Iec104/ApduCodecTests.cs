using Protocols.Iec104;
using Xunit;

namespace UnitTests.Protocols.Iec104;

public sealed class ApduCodecTests
{
    [Fact]
    public Task InformationFrameRoundTrips()
    {
        var apci = Apci.Information(0x1234, 0x0056);
        var parsed = Apci.TryParse(apci.Serialize());
        Assert.Equal(apci, parsed);
        Assert.Equal(ApduFrameKind.Information, parsed!.Value.Kind);
        Assert.Equal(0x1234, parsed.Value.SendSequence);
        Assert.Equal(0x0056, parsed.Value.ReceiveSequence);
        return Task.CompletedTask;
    }

    [Fact]
    public Task SupervisoryAndUnnumberedFramesRoundTrip()
    {
        var supervisory = Apci.Supervisory(0x7ABC);
        var parsedSupervisory = Apci.TryParse(supervisory.Serialize());
        Assert.Equal(ApduFrameKind.Supervisory, parsedSupervisory!.Value.Kind);
        Assert.Equal(0x7ABC, parsedSupervisory.Value.ReceiveSequence);

        foreach (var function in new[] { Apci.StartDtAct, Apci.StartDtCon, Apci.StopDtAct, Apci.StopDtCon, Apci.TestFrAct, Apci.TestFrCon })
        {
            var unnumbered = Apci.Unnumbered(function);
            var parsed = Apci.TryParse(unnumbered.Serialize());
            Assert.Equal(ApduFrameKind.Unnumbered, parsed!.Value.Kind);
            Assert.Equal(function, parsed.Value.UnnumberedFunction);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public Task SequenceNumbersAreFifteenBitAndWrap()
    {
        Assert.Equal(0, Apci.Next(Apci.MaxSequence));
        Assert.Equal(Apci.MaxSequence, Apci.Next(Apci.MaxSequence - 1));
        Assert.Equal(1, Apci.Next(0));
        Assert.Equal(Apci.MaxSequence, Apci.Distance(0, Apci.MaxSequence));
        Assert.Equal(1, Apci.Distance(Apci.MaxSequence, 0));
        Assert.Equal(0, Apci.Distance(5, 5));
        return Task.CompletedTask;
    }

    [Fact]
    public Task ApduSerializeAndParseRoundTrip()
    {
        var apdu = new Apdu(Apci.Information(7, 3), [1, 2, 3, 4, 5]);
        var frame = apdu.Serialize();
        Assert.Equal(Apdu.StartByte, frame[0]);
        Assert.Equal(4 + 5, frame[1]);
        Assert.Equal(frame.Length, 2 + frame[1]);
        var parsed = Apdu.TryParse(frame);
        Assert.Equal(apdu.Control, parsed!.Value.Control);
        Assert.Equal(apdu.Payload, parsed.Value.Payload);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(new byte[] { 0x67, 4, 0, 0, 0, 0 })]                     // wrong start byte
    [InlineData(new byte[] { 0x68, 3, 0, 0, 0 })]                        // length below APCI size
    [InlineData(new byte[] { 0x68, 254, 0, 0, 0, 0 })]                   // length above 253
    [InlineData(new byte[] { 0x68, 4, 0, 0 })]                           // truncated frame
    [InlineData(new byte[] { 0x68, 5, 0, 0, 0, 0 })]                       // length does not match frame size
    [InlineData(new byte[] { 0x68 })]                                    // header only
    [InlineData(new byte[] { })]                                         // empty
    public Task MalformedApduIsRejected(byte[] frame)
    {
        Assert.Null(Apdu.TryParse(frame));
        return Task.CompletedTask;
    }

    [Fact]
    public Task OversizedPayloadIsRejectedOnSerialize()
    {
        var apdu = new Apdu(Apci.Information(0, 0), new byte[Apdu.MaxLength]);
        Assert.Throws<ArgumentOutOfRangeException>(() => apdu.Serialize());
        return Task.CompletedTask;
    }

    [Fact]
    public Task ControlFieldIsLittleEndianWithFormatBits()
    {
        // I-frame, send 1, receive 2: first word = 1 << 1 = 0x0002, second word = 2 << 1 = 0x0004.
        Assert.Equal(new byte[] { 0x02, 0x00, 0x04, 0x00 }, Apci.Information(1, 2).Serialize());
        // S-frame, receive 2: first word = 0x0001, second word = 2 << 1 = 0x0004.
        Assert.Equal(new byte[] { 0x01, 0x00, 0x04, 0x00 }, Apci.Supervisory(2).Serialize());
        // U-frame TESTFR act keeps its control octet.
        Assert.Equal(new byte[] { 0x43, 0x00, 0x00, 0x00 }, Apci.Unnumbered(Apci.TestFrAct).Serialize());
        return Task.CompletedTask;
    }
}
