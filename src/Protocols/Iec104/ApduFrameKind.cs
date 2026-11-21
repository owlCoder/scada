namespace Protocols.Iec104;

/// <summary>APDU frame format selected by the two low bits of the first control-field octet.</summary>
public enum ApduFrameKind : byte
{
    Information = 0,
    Supervisory = 1,
    Unnumbered = 3
}
