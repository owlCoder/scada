namespace Protocols.Iec104;

/// <summary>Cause of transmission (COT) values used by this slave.</summary>
public enum CauseOfTransmission : byte
{
    Spontaneous = 3,
    EndOfInitialization = 4,
    ActivationRequest = 6,
    ActivationConfirm = 7,
    ActivationConfirmNegative = 8,
    DeactivationConfirm = 9,
    ActivationTermination = 10,
    InterrogationResponse = 20,
    UnknownTypeIdentification = 44,
    UnknownCauseOfTransmission = 45,
    UnknownCommonAddress = 46,
    UnknownInformationObjectAddress = 47
}
