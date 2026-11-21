namespace Protocols.Iec104;

/// <summary>IEC 60870-5-104 ASDU type identifications supported by this slave.</summary>
public enum Iec104TypeId : byte
{
    M_SP_NA_1 = 1,   // single point, no time tag
    M_DP_NA_1 = 3,   // double point, no time tag
    M_ST_NA_1 = 5,   // step position, no time tag
    M_ME_NC_1 = 13,  // measured value, short float, no time tag
    M_SP_TB_1 = 30,  // single point with CP56Time2a
    M_DP_TB_1 = 31,  // double point with CP56Time2a
    M_ST_TB_1 = 32,  // step position with CP56Time2a
    M_ME_TF_1 = 36,  // measured value, short float, with CP56Time2a
    C_DC_NA_1 = 46,  // double command, no time tag
    C_RC_NA_1 = 47,  // regulating step command
    C_DC_TA_1 = 59,  // double command with CP56Time2a
    C_IC_NA_1 = 100, // interrogation command
    C_CS_NA_1 = 103, // clock synchronisation command
    M_EI_NA_1 = 70   // end of initialization, slave->master, COT 4 (sent after an address-map rebuild)
}
