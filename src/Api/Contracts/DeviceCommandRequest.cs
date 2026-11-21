using Application.Commands;

namespace Api.Contracts;

public sealed record DeviceCommandRequest(string DeviceId, OperatorCommandType Type, double? Value = null, long? SequenceNumber = null);
