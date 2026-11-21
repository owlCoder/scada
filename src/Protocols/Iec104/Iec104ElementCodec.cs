using Domain.Grid.Snapshots;
using Domain.Process.Devices;

namespace Protocols.Iec104;

/// <summary>
/// Element data sizes and value codecs for the supported type identifications. Quality bytes share the IEC 60870-5
/// layout: bit 4 blocked, bit 5 substituted, bit 6 not topical, bit 7 invalid.
/// </summary>
public static class Iec104ElementCodec
{
    public const byte QualityBlocked = 0x10;
    public const byte QualitySubstituted = 0x20;
    public const byte QualityNotTopical = 0x40;
    public const byte QualityInvalid = 0x80;
    public const byte QualityOverflow = 0x01;

    public const int DoublePointOpen = 1;
    public const int DoublePointClosed = 2;

    /// <summary>Data size of one information element for a known type, excluding the 3-octet IOA; null when unknown.</summary>
    public static int? DataSize(Iec104TypeId type) => type switch
    {
        Iec104TypeId.M_SP_NA_1 or Iec104TypeId.M_DP_NA_1 or Iec104TypeId.M_ST_NA_1 => 1,
        Iec104TypeId.M_ME_NC_1 => 5,
        Iec104TypeId.M_SP_TB_1 or Iec104TypeId.M_DP_TB_1 or Iec104TypeId.M_ST_TB_1 => 1 + Cp56Time2a.Size,
        Iec104TypeId.M_ME_TF_1 => 5 + Cp56Time2a.Size, // short float + QDP + CP56Time2a
        Iec104TypeId.C_DC_NA_1 or Iec104TypeId.C_RC_NA_1 or Iec104TypeId.C_IC_NA_1 => 1,
        Iec104TypeId.M_EI_NA_1 => 1, // local parameter change octet
        Iec104TypeId.C_CS_NA_1 => Cp56Time2a.Size,
        Iec104TypeId.C_DC_TA_1 => 1 + Cp56Time2a.Size,
        _ => null
    };

    public static bool IsKnown(Iec104TypeId type) => DataSize(type) is not null;

    public static byte QualityByte(MeasurementQuality quality) => quality switch
    {
        MeasurementQuality.Good => 0,
        MeasurementQuality.Uncertain => QualitySubstituted,
        _ => QualityInvalid | QualityNotTopical
    };

    public static byte EncodeSinglePoint(int value, byte quality) => (byte)((value & 0x01) | (quality & 0xF0));

    public static byte EncodeDoublePoint(int doublePoint, byte quality) => (byte)((doublePoint & 0x03) | (quality & 0xF0));

    public static byte EncodeStepPosition(int value, bool transient) => (byte)((value & 0x7F) | (transient ? 0x80 : 0));

    public static byte[] EncodeMeasurement(float value, byte quality)
    {
        var data = new byte[5];
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data, value);
        data[4] = quality;
        return data;
    }

    public static byte[] EncodeWithTime(byte value, DateTimeOffset timestamp) =>
        [value, .. Cp56Time2a.Encode(timestamp)];

    public static byte[] EncodeWithTime(byte[] data, DateTimeOffset timestamp)
    {
        var result = new byte[data.Length + Cp56Time2a.Size];
        data.CopyTo(result, 0);
        Cp56Time2a.Encode(timestamp).CopyTo(result, data.Length);
        return result;
    }

    public static int DecodeSinglePoint(byte siq) => siq & 0x01;

    public static int DecodeDoublePoint(byte diq) => diq & 0x03;

    public static int DecodeStepPosition(byte vsi) => vsi & 0x7F;

    public static bool DecodeStepTransient(byte vsi) => (vsi & 0x80) != 0;

    public static float DecodeMeasurement(ReadOnlySpan<byte> data) =>
        System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(data);

    /// <summary>Double command / regulating step control byte: bit 7 select/execute, bits 0-1 command state.</summary>
    public static (bool Select, int State) DecodeControlByte(byte control) => ((control & 0x80) != 0, control & 0x03);
}
