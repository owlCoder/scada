namespace Protocols.Iec104;

/// <summary>
/// Encodes a point sample as an information element. General interrogation responses use the no-time-tag variants
/// (M_ME_NC_1, M_DP_NA_1, M_ST_NA_1); spontaneous transmissions and single points carry a CP56Time2a tag
/// (M_ME_TF_1, M_DP_TB_1, M_ST_TB_1, M_SP_TB_1).
/// </summary>
public static class Iec104SampleEncoder
{
    public static (Iec104TypeId Type, byte[] Data) Encode(Iec104Sample sample, bool withTimeTag, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return sample.Kind switch
        {
            Iec104PointKind.Measurement => withTimeTag
                ? (Iec104TypeId.M_ME_TF_1,
                    Iec104ElementCodec.EncodeWithTime(Iec104ElementCodec.EncodeMeasurement((float)(sample.Measurement ?? 0), sample.Quality), timestamp))
                : (Iec104TypeId.M_ME_NC_1, Iec104ElementCodec.EncodeMeasurement((float)(sample.Measurement ?? 0), sample.Quality)),
            Iec104PointKind.SwitchStatus => withTimeTag
                ? (Iec104TypeId.M_DP_TB_1, Iec104ElementCodec.EncodeWithTime(
                    [Iec104ElementCodec.EncodeDoublePoint(sample.DoublePoint ?? 0, sample.Quality)], timestamp))
                : (Iec104TypeId.M_DP_NA_1, [Iec104ElementCodec.EncodeDoublePoint(sample.DoublePoint ?? 0, sample.Quality)]),
            Iec104PointKind.TapPosition => withTimeTag
                ? (Iec104TypeId.M_ST_TB_1, Iec104ElementCodec.EncodeWithTime(
                    [Iec104ElementCodec.EncodeStepPosition(sample.Step ?? 0, sample.StepTransient)], timestamp))
                : (Iec104TypeId.M_ST_NA_1, [Iec104ElementCodec.EncodeStepPosition(sample.Step ?? 0, sample.StepTransient)]),
            _ => (Iec104TypeId.M_SP_TB_1, Iec104ElementCodec.EncodeWithTime(
                [Iec104ElementCodec.EncodeSinglePoint(sample.SinglePoint ?? 0, sample.Quality)], timestamp))
        };
    }
}
