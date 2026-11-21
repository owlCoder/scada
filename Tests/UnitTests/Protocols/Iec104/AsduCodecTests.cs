using Protocols.Iec104;
using Xunit;

namespace UnitTests.Protocols.Iec104;

public sealed class AsduCodecTests
{
    private static Asdu Build(Iec104TypeId type, bool sq, CauseOfTransmission cot, int oa, int ca,
        params Iec104InformationObject[] objects) =>
        Asdu.Build(new AsduHeader(type, sq, objects.Length, cot, oa, ca), objects);

    [Theory]
    [InlineData(Iec104TypeId.M_SP_NA_1, 1)]
    [InlineData(Iec104TypeId.M_DP_NA_1, 1)]
    [InlineData(Iec104TypeId.M_ST_NA_1, 1)]
    [InlineData(Iec104TypeId.M_ME_NC_1, 5)]
    [InlineData(Iec104TypeId.M_SP_TB_1, 8)]
    [InlineData(Iec104TypeId.M_DP_TB_1, 8)]
    [InlineData(Iec104TypeId.M_ST_TB_1, 8)]
    [InlineData(Iec104TypeId.M_ME_TF_1, 12)]
    [InlineData(Iec104TypeId.C_DC_NA_1, 1)]
    [InlineData(Iec104TypeId.C_RC_NA_1, 1)]
    [InlineData(Iec104TypeId.C_DC_TA_1, 8)]
    [InlineData(Iec104TypeId.C_IC_NA_1, 1)]
    [InlineData(Iec104TypeId.C_CS_NA_1, 7)]
    public Task ElementDataSizes(Iec104TypeId type, int size)
    {
        Assert.Equal(size, Iec104ElementCodec.DataSize(type));
        Assert.True(Iec104ElementCodec.IsKnown(type));
        return Task.CompletedTask;
    }

    [Fact]
    public Task UnknownTypeHasNoDataSize()
    {
        Assert.Null(Iec104ElementCodec.DataSize((Iec104TypeId)200));
        Assert.False(Iec104ElementCodec.IsKnown((Iec104TypeId)200));
        return Task.CompletedTask;
    }

    [Fact]
    public Task AsduRoundTripsWithIndividualIoas()
    {
        var timestamp = new DateTimeOffset(2026, 3, 4, 5, 6, 7, 890, TimeSpan.Zero);
        var measurement = Iec104ElementCodec.EncodeWithTime(Iec104ElementCodec.EncodeMeasurement(10.5f, 0), timestamp);
        var asdu = Build(Iec104TypeId.M_ME_TF_1, false, CauseOfTransmission.Spontaneous, 0, 2,
            new Iec104InformationObject(1000, measurement),
            new Iec104InformationObject(1001, measurement));
        var parsed = Asdu.TryParse(asdu.Serialize());
        Assert.NotNull(parsed);
        Assert.Equal(Iec104TypeId.M_ME_TF_1, parsed!.Header.Type);
        Assert.False(parsed.Header.SingleStructure);
        Assert.Equal(2, parsed.Header.ElementCount);
        Assert.Equal(CauseOfTransmission.Spontaneous, parsed.Header.Cause);
        Assert.Equal(0, parsed.Header.OriginatorAddress);
        Assert.Equal(2, parsed.Header.CommonAddress);
        Assert.Equal(2, parsed.Objects.Count);
        Assert.Equal(1000, parsed.Objects[0].InformationObjectAddress);
        Assert.Equal(1001, parsed.Objects[1].InformationObjectAddress);
        Assert.Equal(measurement, parsed.Objects[0].Data);
        Assert.Equal(asdu.Payload, parsed.Payload);
        return Task.CompletedTask;
    }

    [Fact]
    public Task AsduRoundTripsWithSingleStructure()
    {
        var asdu = Build(Iec104TypeId.M_DP_NA_1, true, CauseOfTransmission.InterrogationResponse, 0, 1,
            new Iec104InformationObject(0, [Iec104ElementCodec.EncodeDoublePoint(2, 0)]),
            new Iec104InformationObject(1, [Iec104ElementCodec.EncodeDoublePoint(1, 0)]));
        var frame = asdu.Serialize();
        // SQ set (0x80) + count 2 in the second header octet, no IOAs in the payload: 6 header + 2 data bytes.
        Assert.Equal(0x82, frame[1]);
        Assert.Equal(8, frame.Length);
        var parsed = Asdu.TryParse(frame);
        Assert.NotNull(parsed);
        Assert.True(parsed!.Header.SingleStructure);
        Assert.Equal(2, parsed.Objects.Count);
        Assert.Equal(0, parsed.Objects[0].InformationObjectAddress);
        Assert.Equal(2, Iec104ElementCodec.DecodeDoublePoint(parsed.Objects[0].Data[0]));
        Assert.Equal(1, Iec104ElementCodec.DecodeDoublePoint(parsed.Objects[1].Data[0]));
        return Task.CompletedTask;
    }

    [Fact]
    public Task HeaderEncodesCommonAddressLittleEndian()
    {
        var asdu = Build(Iec104TypeId.C_IC_NA_1, false, CauseOfTransmission.ActivationRequest, 3, 0x0201,
            new Iec104InformationObject(0, [20]));
        var frame = asdu.Serialize();
        Assert.Equal((byte)Iec104TypeId.C_IC_NA_1, frame[0]);
        Assert.Equal(1, frame[1] & 0x7F);
        Assert.Equal((byte)CauseOfTransmission.ActivationRequest, frame[2]);
        Assert.Equal(3, frame[3]);
        Assert.Equal(0x01, frame[4]);
        Assert.Equal(0x02, frame[5]);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(new byte[] { })]                                              // empty
    [InlineData(new byte[] { 1, 1, 6, 0, 1 })]                                // header too short
    [InlineData(new byte[] { 100, 0, 6, 0, 1, 0 })]                           // element count 0
    [InlineData(new byte[] { 100, 0x80, 6, 0, 1, 0 })]                        // count 0 with SQ bit
    [InlineData(new byte[] { 13, 2, 6, 0, 1, 0, 1, 2, 3, 4, 5 })]             // payload too short for 2 x M_ME_NC_1
    [InlineData(new byte[] { 13, 1, 6, 0, 1, 0, 1, 2, 3, 4, 5, 6 })]          // payload too long for 1 x M_ME_NC_1
    [InlineData(new byte[] { 46, 1, 6, 0, 1, 0, 0, 1, 2 })]                   // C_DC_NA_1 with 2 payload bytes
    public Task MalformedAsduIsRejected(byte[] frame)
    {
        Assert.Null(Asdu.TryParse(frame));
        return Task.CompletedTask;
    }

    [Fact]
    public Task UnknownTypeKeepsRawPayloadForEcho()
    {
        var payload = new byte[] { 200, 1, 6, 0, 1, 0, 9, 9, 9 };
        var parsed = Asdu.TryParse(payload);
        Assert.NotNull(parsed);
        Assert.Equal((Iec104TypeId)200, parsed!.Header.Type);
        Assert.Empty(parsed.Objects);
        Assert.Equal(payload[6..], parsed.Payload);
        return Task.CompletedTask;
    }

    [Fact]
    public Task MeasurementValuesRoundTripThroughCodec()
    {
        var data = Iec104ElementCodec.EncodeMeasurement(-12.25f, Iec104ElementCodec.QualityInvalid);
        Assert.Equal(5, data.Length);
        Assert.Equal(-12.25f, Iec104ElementCodec.DecodeMeasurement(data));
        Assert.Equal(Iec104ElementCodec.QualityInvalid, data[4]);
        return Task.CompletedTask;
    }

    [Fact]
    public Task ControlBytesDecodeSelectExecuteAndStates()
    {
        Assert.Equal((true, 1), Iec104ElementCodec.DecodeControlByte(0x81));  // select open
        Assert.Equal((true, 2), Iec104ElementCodec.DecodeControlByte(0x82));  // select close
        Assert.Equal((false, 1), Iec104ElementCodec.DecodeControlByte(0x01)); // execute open
        Assert.Equal((false, 2), Iec104ElementCodec.DecodeControlByte(0x02)); // execute close
        Assert.Equal((false, 0), Iec104ElementCodec.DecodeControlByte(0x00)); // deactivation
        Assert.Equal((false, 3), Iec104ElementCodec.DecodeControlByte(0x03)); // not used
        Assert.Equal(1, Iec104ElementCodec.DecodeSinglePoint(Iec104ElementCodec.EncodeSinglePoint(1, 0)));
        Assert.Equal(0, Iec104ElementCodec.DecodeSinglePoint(Iec104ElementCodec.EncodeSinglePoint(0, 0x80)));
        Assert.Equal(2, Iec104ElementCodec.DecodeDoublePoint(Iec104ElementCodec.EncodeDoublePoint(2, 0)));
        Assert.Equal(5, Iec104ElementCodec.DecodeStepPosition(Iec104ElementCodec.EncodeStepPosition(5, false)));
        Assert.True(Iec104ElementCodec.DecodeStepTransient(Iec104ElementCodec.EncodeStepPosition(5, true)));
        return Task.CompletedTask;
    }

    [Fact]
    public Task BuildRejectsEmptyAndOversizedObjectLists()
    {
        var header = new AsduHeader(Iec104TypeId.M_DP_NA_1, false, 1, CauseOfTransmission.InterrogationResponse, 0, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => Asdu.Build(header, []));
        var tooMany = Enumerable.Range(0, 200)
            .Select(i => new Iec104InformationObject(i, [1]))
            .ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => Asdu.Build(header, tooMany));
        var tooBig = Enumerable.Range(0, 100)
            .Select(i => new Iec104InformationObject(i, new byte[10]))
            .ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => Asdu.Build(header, tooBig));
        return Task.CompletedTask;
    }
}
